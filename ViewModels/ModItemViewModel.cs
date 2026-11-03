using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VSModManager.Services;

namespace VSModManager.ViewModels;

public enum ModStatus
{
    Checking,
    UpToDate,
    UpdateAvailable,
    NotOnModDb,
    NoCompatibleRelease,
    NoModInfo,
    Updating,
    Updated,
    Error,
}

/// <summary>One row in the list: every installed file sharing a modid.</summary>
public sealed partial class ModItemViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public ModItemViewModel(MainViewModel owner, IEnumerable<InstalledMod> files)
    {
        _owner = owner;
        Files = files.OrderByDescending(f => f.SemVersion).ToList();
        _status = ModId is null ? ModStatus.NoModInfo : ModStatus.Checking;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Primary), nameof(Name), nameof(InstalledVersion), nameof(IsDuplicate), nameof(FileNames), nameof(DuplicateText), nameof(HasChangelog), nameof(ChangelogText))]
    private List<InstalledMod> _files;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(HasPage))]
    private ModDbMod? _remote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestVersion), nameof(HasChangelog), nameof(ChangelogText))]
    private ModDbRelease? _latest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangelogButtonText))]
    private bool _isChangelogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBrush), nameof(CanUpdate))]
    private ModStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencyIssues), nameof(HasFixableDependencies), nameof(DependencyText))]
    private List<DependencyIssue> _dependencyIssues = [];

    private bool _isEnabled = true;

    /// <summary>Bound two-way to the row's checkbox; writes clientsettings.json.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (value == _isEnabled) return;
            if (_owner.TrySetEnabled(this, value))
                SetEnabledSilently(value);
            else
                // Refused (e.g. game running): bounce the checkbox back.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsEnabled)));
        }
    }

    /// <summary>Updates the flag without touching clientsettings.json.</summary>
    public void SetEnabledSilently(bool value)
    {
        _isEnabled = value;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(RowOpacity));
    }

    public double RowOpacity => IsEnabled ? 1.0 : 0.5;

    [ObservableProperty]
    private bool _isInstallingDependencies;

    public InstalledMod Primary => Files[0];
    public string? ModId => Primary.ModId;
    public string Name => Remote?.Name ?? Primary.Name;
    public string InstalledVersion => Primary.Version ?? "?";
    public string LatestVersion => Latest?.ModVersion ?? "—";
    public bool IsDuplicate => Files.Count > 1;
    public string DuplicateText => $"{Files.Count} copies installed";
    public string FileNames => string.Join("\n", Files.Select(f => f.FileName));
    public bool HasPage => Remote is not null;
    public bool CanUpdate => Status == ModStatus.UpdateAvailable;
    public bool HasDependencyIssues => DependencyIssues.Count > 0;
    public bool HasFixableDependencies => DependencyIssues.Any(d => d.Problem is DependencyProblem.Missing or DependencyProblem.Disabled);
    public string DependencyText => string.Join("\n", DependencyIssues.Select(d => d.Describe()));

    /// <summary>Releases after the installed version, up to the latest compatible one, newest first.</summary>
    private List<ModDbRelease> NewerReleases =>
        Remote is null || Latest?.Version is not { } latest
            ? []
            : Remote.Releases
                .Where(r => r.Version is { } v
                         && v.CompareTo(latest) <= 0
                         && (Primary.SemVersion is not { } have || v.CompareTo(have) > 0))
                .OrderByDescending(r => r.Version)
                .ToList();

    public bool HasChangelog => NewerReleases.Count > 0;
    public string ChangelogButtonText => IsChangelogOpen ? "Hide" : "What's New";

    public string ChangelogText => string.Join("\n\n", NewerReleases.Select(r =>
    {
        var date = r.Created?.Split(' ')[0];
        var notes = HtmlText.ToPlainText(r.Changelog);
        return $"{r.ModVersion}  ({date})\n{(notes.Length > 0 ? notes : "No changelog provided.")}";
    }));

    public string StatusText => Status switch
    {
        ModStatus.Checking => "Checking…",
        ModStatus.UpToDate => "Up to date",
        ModStatus.UpdateAvailable => "Update available",
        ModStatus.NotOnModDb => "Not on ModDB",
        ModStatus.NoCompatibleRelease => "No release for this game version",
        ModStatus.NoModInfo => "No modinfo.json",
        ModStatus.Updating => "Updating…",
        ModStatus.Updated => "Updated",
        ModStatus.Error => "Error: " + ErrorMessage,
        _ => Status.ToString(),
    };

    public IBrush StatusBrush => Status switch
    {
        ModStatus.UpToDate or ModStatus.Updated => Brushes.MediumSeaGreen,
        ModStatus.UpdateAvailable => Brushes.Orange,
        ModStatus.Error => Brushes.IndianRed,
        _ => Brushes.Gray,
    };

    public async Task CheckAsync(ModDbClient client, SemVer? gameVersion)
    {
        if (ModId is null) return;

        Status = ModStatus.Checking;
        try
        {
            Remote = await client.GetModAsync(ModId);
            if (Remote is null)
            {
                Status = ModStatus.NotOnModDb;
                return;
            }

            Latest = Remote.LatestCompatible(gameVersion);
            var installed = Primary.SemVersion;

            Status = Latest switch
            {
                null => ModStatus.NoCompatibleRelease,
                _ when installed is null || Latest.Version!.CompareTo(installed) > 0 => ModStatus.UpdateAvailable,
                _ => ModStatus.UpToDate,
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Status = ModStatus.Error;
        }
    }

    [RelayCommand]
    public async Task UpdateAsync()
    {
        if (Latest is null || !CanUpdate) return;

        Status = ModStatus.Updating;
        try
        {
            var oldFiles = Files;
            var target = await _owner.Updater.InstallAsync(Latest, oldFiles);
            var installed = ModScanner.ReadZip(target);
            Files = [installed];
            Status = ModStatus.Updated;
            IsChangelogOpen = false;

            // disabledMods entries are version-specific ("healthbar@1.4.2"), so
            // without this a disabled mod would silently re-enable on update.
            try
            {
                _owner.Settings.Replace(oldFiles, installed, keepDisabled: !IsEnabled);
            }
            catch (Exception ex)
            {
                _owner.StatusMessage = $"Updated {Name}, but couldn't update disabled-mod list: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Status = ModStatus.Error;
        }
        _owner.OnItemChanged();
        _owner.RecheckDependencies();
    }

    /// <summary>Moves every copy except the highest version into the backup folder.</summary>
    [RelayCommand]
    private void RemoveDuplicates()
    {
        if (GamePaths.IsGameRunning())
        {
            _owner.StatusMessage = "Close Vintage Story before changing mod files.";
            return;
        }

        try
        {
            var extras = Files.Skip(1).ToList();
            foreach (var extra in extras)
                _owner.Updater.Backup(extra.Path);
            Files = [Primary];
            _owner.Settings.Replace(extras, null, keepDisabled: false);
            _owner.StatusMessage = $"Removed older copies of {Name} (moved to backups).";
        }
        catch (Exception ex)
        {
            _owner.StatusMessage = $"Couldn't remove duplicates of {Name}: {ex.Message}";
        }
        _owner.OnItemChanged();
        _owner.RecheckDependencies();
    }

    /// <summary>Enables disabled dependencies and installs missing ones.</summary>
    [RelayCommand]
    private async Task FixDependenciesAsync()
    {
        IsInstallingDependencies = true;
        try
        {
            foreach (var dep in DependencyIssues.ToList())
            {
                if (dep.Problem == DependencyProblem.Disabled && _owner.FindMod(dep.ModId) is { } target)
                    target.IsEnabled = true;
                else if (dep.Problem == DependencyProblem.Missing)
                    await _owner.InstallDependencyAsync(dep.ModId);
            }
        }
        finally
        {
            IsInstallingDependencies = false;
        }
    }

    [RelayCommand]
    private void ToggleChangelog() => IsChangelogOpen = !IsChangelogOpen;

    [RelayCommand]
    private void OpenPage()
    {
        if (Remote is not null)
            Process.Start(new ProcessStartInfo(Remote.PageUrl) { UseShellExecute = true });
    }
}
