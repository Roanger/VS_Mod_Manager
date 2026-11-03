using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VSModManager.Services;

namespace VSModManager.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxParallelChecks = 6;

    private readonly ModDbClient _client = new();

    public GamePaths Paths { get; private set; }
    public ModUpdater Updater { get; private set; }
    public ClientSettings Settings { get; private set; }
    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(UpdateAllCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    private int _updatesAvailable;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _dependencyIssueCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    private bool _isGameRunning;

    public string PlayText => IsGameRunning ? "Running…" : "▶ Play";

    private readonly Avalonia.Threading.DispatcherTimer _gameWatch = new() { Interval = TimeSpan.FromSeconds(2) };

    private readonly ProfileStore _profiles = ProfileStore.Load();
    public ObservableCollection<string> ProfileNames { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileName = "";

    /// <summary>Bound to the profile dropdown; selecting one applies it.</summary>
    public string? SelectedProfile
    {
        get => _profiles.Active;
        set
        {
            if (value is null || string.Equals(value, _profiles.Active, StringComparison.OrdinalIgnoreCase)) return;
            if (!SwitchProfile(value))
                // Refused (e.g. game running): put the dropdown back.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedProfile)));
        }
    }

    public string GameVersionText => Paths.GameVersion is { } v ? $"Vintage Story {v}" : "Game version not detected";
    public string ModsDir => Paths.ModsDir;

    public MainViewModel()
    {
        Paths = new GamePaths(AppSettings.Load());
        Updater = new ModUpdater(Paths, _client);
        Settings = new ClientSettings(Paths);

        // Fill the dropdown before the window binds to it; repopulating it later
        // leaves the ComboBox with no selection.
        if (_profiles.Profiles.Count == 0)
            _profiles.Profiles.Add(new Profile { Name = ProfileStore.DefaultName });
        if (_profiles.ActiveProfile is null)
            _profiles.Active = _profiles.Profiles[0].Name;
        foreach (var p in _profiles.Profiles)
            ProfileNames.Add(p.Name);

        IsGameRunning = GamePaths.IsGameRunning();
        _gameWatch.Tick += (_, _) => WatchGame();
        _gameWatch.Start();
    }

    private void WatchGame()
    {
        bool running = GamePaths.IsGameRunning();
        if (running == IsGameRunning) return;

        IsGameRunning = running;
        if (running)
            StatusMessage = "Vintage Story is running; mod changes are paused until it closes.";
        // The game may have changed mods or disabledMods while open.
        else if (RefreshCommand.CanExecute(null))
            RefreshCommand.Execute(null);
    }

    /// <summary>Saves new folder settings and reloads everything against them.</summary>
    public async Task ApplySettingsAsync(AppSettings settings)
    {
        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            StatusMessage = "Couldn't save settings: " + ex.Message;
            return;
        }

        Paths = new GamePaths(settings);
        Updater = new ModUpdater(Paths, _client);
        Settings = new ClientSettings(Paths);
        OnPropertyChanged(nameof(GameVersionText));
        OnPropertyChanged(nameof(ModsDir));

        if (RefreshCommand.CanExecute(null))
            await RefreshCommand.ExecuteAsync(null);
        else
            StatusMessage = "Settings saved. Click Refresh once the current scan finishes.";
    }

    private bool CanPlay => !IsGameRunning;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (!File.Exists(Paths.ExePath))
        {
            StatusMessage = $"Couldn't find {Paths.ExePath}.";
            return;
        }

        try
        {
            Paths.LaunchGame();
            IsGameRunning = true;
            StatusMessage = "Launching Vintage Story…";
        }
        catch (Exception ex)
        {
            StatusMessage = "Couldn't launch the game: " + ex.Message;
        }
    }

    public Task InitializeAsync() => RefreshAsync();

    private bool NotBusy => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Scanning mods folder…";
            var installed = await Task.Run(() => ModScanner.Scan(Paths.ModsDir));

            Mods.Clear();
            var groups = installed
                .GroupBy(m => m.ModId ?? "\0" + m.Path)
                .Select(g => new ModItemViewModel(this, g))
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
            var disabled = Settings.LoadDisabled();
            foreach (var item in groups)
            {
                item.SetEnabledSilently(!ClientSettings.IsDisabled(item.Primary, disabled));
                Mods.Add(item);
            }
            RecheckDependencies();
            SyncActiveProfile();

            StatusMessage = $"Checking {Mods.Count} mods against the ModDB…";
            using var gate = new SemaphoreSlim(MaxParallelChecks);
            await Task.WhenAll(Mods.Select(async item =>
            {
                await gate.WaitAsync();
                try { await item.CheckAsync(_client, Paths.GameVersion); }
                finally { gate.Release(); }
            }));

            OnItemChanged();
            StatusMessage = UpdatesAvailable == 0
                ? $"{Mods.Count} mods, all up to date."
                : $"{Mods.Count} mods, {UpdatesAvailable} update(s) available.";
            if (DependencyIssueCount > 0)
                StatusMessage += $" {DependencyIssueCount} mod(s) have dependency problems.";
            if (Paths.GameVersion is null)
                StatusMessage = $"Vintage Story wasn't found in {Paths.InstallDir}. Open Settings to choose its folder.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Refresh failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUpdateAll => !IsBusy && UpdatesAvailable > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private async Task UpdateAllAsync()
    {
        if (GamePaths.IsGameRunning())
        {
            StatusMessage = "Close Vintage Story before updating mods.";
            return;
        }

        IsBusy = true;
        try
        {
            var pending = Mods.Where(m => m.CanUpdate).ToList();
            for (int i = 0; i < pending.Count; i++)
            {
                StatusMessage = $"Updating {pending[i].Name} ({i + 1}/{pending.Count})…";
                await pending[i].UpdateAsync();
            }

            var failed = pending.Count(m => m.Status == ModStatus.Error);
            StatusMessage = failed == 0
                ? $"Updated {pending.Count} mod(s). Old versions are in ModManagerBackups."
                : $"Updated {pending.Count - failed} mod(s), {failed} failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenModsFolder() => OpenFolder(Paths.ModsDir);

    [RelayCommand]
    private void OpenBackupsFolder()
    {
        Directory.CreateDirectory(Paths.BackupDir);
        OpenFolder(Paths.BackupDir);
    }

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void OnItemChanged()
    {
        UpdatesAvailable = Mods.Count(m => m.CanUpdate);
        DuplicateCount = Mods.Count(m => m.IsDuplicate);
    }

    public void RecheckDependencies()
    {
        var withId = Mods.Where(m => m.ModId is not null).ToList();
        var installed = withId.ToDictionary(m => m.ModId!, m => m.Primary, StringComparer.OrdinalIgnoreCase);
        var disabled = withId.Where(m => !m.IsEnabled).Select(m => m.ModId!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A disabled mod isn't loaded, so its own requirements don't matter.
        foreach (var item in Mods)
            item.DependencyIssues = item.IsEnabled
                ? DependencyChecker.Check(item.Primary, installed, disabled, Paths.GameVersion)
                : [];

        DependencyIssueCount = Mods.Count(m => m.HasDependencyIssues);
    }

    public ModItemViewModel? FindMod(string modId) =>
        Mods.FirstOrDefault(m => string.Equals(m.ModId, modId, StringComparison.OrdinalIgnoreCase));

    public bool TrySetEnabled(ModItemViewModel item, bool enabled)
    {
        if (item.ModId is null)
        {
            StatusMessage = $"{item.Name} has no modinfo.json, so the game can't enable or disable it.";
            return false;
        }

        try
        {
            Settings.SetDisabled(item.Files, !enabled);
            StatusMessage = $"{(enabled ? "Enabled" : "Disabled")} {item.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return false;
        }

        // Defer so the row's own flag is updated first.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RecheckDependencies();
            SyncActiveProfile();
        });
        return true;
    }

    private IEnumerable<ModItemViewModel> ToggleableMods => Mods.Where(m => m.ModId is not null);

    /// <summary>
    /// Saves the current enabled/disabled state into the active profile. Runs on every
    /// refresh too, so changes made on the in-game mod screen are absorbed.
    /// </summary>
    private void SyncActiveProfile()
    {
        if (_profiles.ActiveProfile is not { } active) return;

        // Remember choices for mods that aren't installed right now, in case they come back.
        var installedIds = ToggleableMods.Select(m => m.ModId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        active.DisabledMods.RemoveWhere(installedIds.Contains);
        active.DisabledMods.UnionWith(ToggleableMods.Where(m => !m.IsEnabled).Select(m => m.ModId!));

        TrySaveProfiles();
    }

    private bool SwitchProfile(string name)
    {
        if (_profiles.Find(name) is not { } profile) return false;

        try
        {
            Settings.ApplyProfile(ToggleableMods.Select(m => (IReadOnlyList<InstalledMod>)m.Files), profile.DisabledMods);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return false;
        }

        foreach (var item in ToggleableMods)
            item.SetEnabledSilently(!profile.DisabledMods.Contains(item.ModId!));

        _profiles.Active = profile.Name;
        TrySaveProfiles();
        OnPropertyChanged(nameof(SelectedProfile));
        RecheckDependencies();

        var off = ToggleableMods.Count(m => !m.IsEnabled);
        StatusMessage = $"Switched to profile \"{profile.Name}\" ({off} mod(s) disabled).";
        return true;
    }

    private bool CanCreateProfile =>
        !string.IsNullOrWhiteSpace(NewProfileName) && _profiles.Find(NewProfileName.Trim()) is null;

    /// <summary>Creates a profile from the current state and makes it active.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateProfile))]
    private void CreateProfile()
    {
        var profile = new Profile { Name = NewProfileName.Trim() };
        _profiles.Profiles.Add(profile);
        _profiles.Active = profile.Name;
        SyncActiveProfile();

        ProfileNames.Add(profile.Name);
        OnPropertyChanged(nameof(SelectedProfile));
        NewProfileName = "";
        StatusMessage = $"Created profile \"{profile.Name}\" from the current mod setup.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (_profiles.ActiveProfile is not { } active) return;
        if (_profiles.Profiles.Count == 1)
        {
            StatusMessage = "Can't delete the only profile.";
            return;
        }

        var fallback = _profiles.Profiles.First(p => p != active);
        if (!SwitchProfile(fallback.Name)) return;

        _profiles.Profiles.Remove(active);
        ProfileNames.Remove(active.Name);
        TrySaveProfiles();
        StatusMessage = $"Deleted profile \"{active.Name}\"; now using \"{fallback.Name}\".";
    }

    private void TrySaveProfiles()
    {
        try { _profiles.Save(); }
        catch (Exception ex) { StatusMessage = "Couldn't save profiles: " + ex.Message; }
    }

    /// <summary>
    /// Installs the latest compatible release of a missing dependency, then
    /// recursively installs anything that mod itself is missing.
    /// </summary>
    public async Task InstallDependencyAsync(string modId)
    {
        if (FindMod(modId) is not null)
            return;

        try
        {
            StatusMessage = $"Installing dependency {modId}…";
            var remote = await _client.GetModAsync(modId);
            if (remote is null)
            {
                StatusMessage = $"Dependency {modId} isn't on the ModDB; install it manually.";
                return;
            }

            var release = remote.LatestCompatible(Paths.GameVersion);
            if (release is null)
            {
                StatusMessage = $"{remote.Name} has no release for {GameVersionText}.";
                return;
            }

            var target = await Updater.InstallAsync(release, []);
            var item = new ModItemViewModel(this, [ModScanner.ReadZip(target)]);
            InsertSorted(item);
            await item.CheckAsync(_client, Paths.GameVersion);

            OnItemChanged();
            RecheckDependencies();
            StatusMessage = $"Installed {remote.Name} {release.ModVersion}.";

            foreach (var sub in item.DependencyIssues.Where(d => d.Problem == DependencyProblem.Missing).ToList())
                await InstallDependencyAsync(sub.ModId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't install {modId}: {ex.Message}";
        }
    }

    private void InsertSorted(ModItemViewModel item)
    {
        int i = 0;
        while (i < Mods.Count && string.Compare(Mods[i].Name, item.Name, StringComparison.OrdinalIgnoreCase) < 0)
            i++;
        Mods.Insert(i, item);
    }
}
