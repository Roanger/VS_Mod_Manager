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

    public GamePaths Paths { get; } = new();
    public ModUpdater Updater { get; }
    public ClientSettings Settings { get; }
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

    public string GameVersionText => Paths.GameVersion is { } v ? $"Vintage Story {v}" : "Game version not detected";
    public string ModsDir => Paths.ModsDir;

    public MainViewModel()
    {
        Updater = new ModUpdater(Paths, _client);
        Settings = new ClientSettings(Paths);
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

        // Defer so the row's own flag is updated before dependencies are evaluated.
        Avalonia.Threading.Dispatcher.UIThread.Post(RecheckDependencies);
        return true;
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
