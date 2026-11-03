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
    [NotifyPropertyChangedFor(nameof(Primary), nameof(Name), nameof(InstalledVersion), nameof(IsDuplicate), nameof(FileNames), nameof(DuplicateText))]
    private List<InstalledMod> _files;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(HasPage))]
    private ModDbMod? _remote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestVersion))]
    private ModDbRelease? _latest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBrush), nameof(CanUpdate))]
    private ModStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorMessage;

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
            var target = await _owner.Updater.InstallAsync(Latest, Files);
            Files = [ModScanner.ReadZip(target)];
            Status = ModStatus.Updated;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Status = ModStatus.Error;
        }
        _owner.OnItemChanged();
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
            foreach (var extra in Files.Skip(1))
                _owner.Updater.Backup(extra.Path);
            Files = [Primary];
            _owner.StatusMessage = $"Removed older copies of {Name} (moved to backups).";
        }
        catch (Exception ex)
        {
            _owner.StatusMessage = $"Couldn't remove duplicates of {Name}: {ex.Message}";
        }
        _owner.OnItemChanged();
    }

    [RelayCommand]
    private void OpenPage()
    {
        if (Remote is not null)
            Process.Start(new ProcessStartInfo(Remote.PageUrl) { UseShellExecute = true });
    }
}
