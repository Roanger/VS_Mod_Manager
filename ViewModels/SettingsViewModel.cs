using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VSModManager.Services;

namespace VSModManager.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(GamePaths current)
    {
        _installDir = current.InstallDir;
        _dataDir = current.DataDir;
        Validate();
    }

    [ObservableProperty] private string _installDir;
    [ObservableProperty] private string _dataDir;

    [ObservableProperty] private string? _installMessage;
    [ObservableProperty] private bool _installOk;
    [ObservableProperty] private string? _dataMessage;
    [ObservableProperty] private bool _dataOk;
    [ObservableProperty] private bool _dataWarning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canSave;

    /// <summary>Set by Save; the window closes with this as its result.</summary>
    public AppSettings? Result { get; private set; }
    public event Action? CloseRequested;

    partial void OnInstallDirChanged(string value) => Validate();
    partial void OnDataDirChanged(string value) => Validate();

    private void Validate()
    {
        var installError = GamePaths.ValidateInstallDir(InstallDir);
        InstallOk = installError is null;
        InstallMessage = installError
            ?? $"Found Vintage Story {GamePaths.DetectGameVersion(InstallDir)?.ToString() ?? "(version unknown)"}";

        var (dataError, warning) = GamePaths.ValidateDataDir(DataDir);
        DataOk = dataError is null && warning is null;
        DataWarning = dataError is null && warning is not null;
        DataMessage = dataError ?? warning ?? "Looks good.";

        CanSave = installError is null && dataError is null;
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        InstallDir = GamePaths.DefaultInstallDir;
        DataDir = GamePaths.DefaultDataDir;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // Store defaults as null so they keep following auto-detection.
        static string? OrNull(string path, string def)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(full, Path.TrimEndingDirectorySeparator(def), cmp) ? null : full;
        }

        Result = new AppSettings
        {
            InstallDir = OrNull(InstallDir, GamePaths.DefaultInstallDir),
            DataDir = OrNull(DataDir, GamePaths.DefaultDataDir),
        };
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
