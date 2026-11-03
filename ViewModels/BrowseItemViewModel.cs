using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VSModManager.Services;

namespace VSModManager.ViewModels;

/// <summary>One mod in the Browse tab.</summary>
public sealed partial class BrowseItemViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public BrowseItemViewModel(MainViewModel main, ModDbListEntry entry)
    {
        _main = main;
        Entry = entry;
        RefreshInstalled();
    }

    public ModDbListEntry Entry { get; }

    /// <summary>The modid used to install; mods without one can only be opened on the ModDB.</summary>
    public string? InstallId => Entry.ModIdStrs.FirstOrDefault();

    public string Name => Entry.Name ?? InstallId ?? "Unnamed mod";
    public string? Summary => Entry.Summary;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Entry.Author)) parts.Add($"by {Entry.Author}");
            parts.Add($"{FormatCount(Entry.Downloads)} downloads");
            if (Entry.Side is "client") parts.Add("client only");
            else if (Entry.Side is "server") parts.Add("server only");
            if (Entry.LastReleased?.Split(' ')[0] is { Length: > 0 } date) parts.Add($"updated {date}");
            return string.Join("  ·  ", parts);
        }
    }

    public string TagText => string.Join(", ", Entry.Tags);
    public bool HasTags => Entry.Tags.Count > 0;

    [ObservableProperty] private Bitmap? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(InstallText))]
    private bool _isInstalling;

    /// <summary>Why the last install attempt failed, shown on the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool CanInstall => !IsInstalled && !IsInstalling && InstallId is not null;
    public string InstallText => IsInstalling ? "Installing…" : "Install";

    public void RefreshInstalled() =>
        IsInstalled = Entry.ModIdStrs.Any(id => _main.FindMod(id) is not null);

    public async Task LoadIconAsync(IconCache icons) =>
        Icon ??= await icons.FromUrlAsync(Entry.Logo);

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (InstallId is null) return;

        IsInstalling = true;
        Note = null;
        try
        {
            if (!await _main.InstallModAsync(InstallId))
                Note = _main.StatusMessage;
        }
        finally
        {
            IsInstalling = false;
            RefreshInstalled();
        }
    }

    [RelayCommand]
    private void OpenPage()
    {
        var url = string.IsNullOrEmpty(Entry.UrlAlias)
            ? $"https://mods.vintagestory.at/show/mod/{Entry.AssetId}"
            : $"https://mods.vintagestory.at/{Entry.UrlAlias}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string FormatCount(int n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}k",
        _ => n.ToString(),
    };
}
