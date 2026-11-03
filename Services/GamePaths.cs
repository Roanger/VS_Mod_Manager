using System.Diagnostics;

namespace VSModManager.Services;

/// <summary>Locates the Vintage Story install, data folder and game version.</summary>
public sealed class GamePaths
{
    public string InstallDir { get; }
    public string DataDir { get; }
    public string ModsDir => Path.Combine(DataDir, "Mods");
    public string BackupDir => Path.Combine(DataDir, "ModManagerBackups");
    public SemVer? GameVersion { get; }

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string DefaultInstallDir => Path.Combine(AppData, "Vintagestory");
    public static string DefaultDataDir => Path.Combine(AppData, "VintagestoryData");

    public GamePaths(string? installDir = null, string? dataDir = null)
    {
        InstallDir = installDir ?? DefaultInstallDir;
        DataDir = dataDir ?? DefaultDataDir;
        GameVersion = DetectGameVersion(InstallDir);
    }

    public GamePaths(AppSettings settings) : this(settings.InstallDir, settings.DataDir) { }

    /// <summary>Returns an error message, or null if the folder holds the game.</summary>
    public static string? ValidateInstallDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "Folder doesn't exist.";
        if (!File.Exists(Path.Combine(dir, "Vintagestory.exe"))) return "No Vintagestory.exe in this folder.";
        return null;
    }

    /// <summary>Returns (error, warning); error blocks saving, warning doesn't.</summary>
    public static (string? Error, string? Warning) ValidateDataDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return ("Folder doesn't exist.", null);
        bool hasMods = Directory.Exists(Path.Combine(dir, "Mods"));
        bool hasSettings = File.Exists(Path.Combine(dir, "clientsettings.json"));
        if (!hasMods && !hasSettings)
            return (null, "No Mods folder or clientsettings.json here; is this the right folder?");
        return (null, null);
    }

    public static SemVer? DetectGameVersion(string installDir)
    {
        var exe = Path.Combine(installDir, "Vintagestory.exe");
        if (!File.Exists(exe)) return null;

        var info = FileVersionInfo.GetVersionInfo(exe);
        return SemVer.Parse(info.ProductVersion) ?? SemVer.Parse(info.FileVersion);
    }

    public string ExePath => Path.Combine(InstallDir, "Vintagestory.exe");

    public static bool IsGameRunning() =>
        Process.GetProcessesByName("Vintagestory").Length > 0;

    public void LaunchGame() =>
        Process.Start(new ProcessStartInfo(ExePath) { WorkingDirectory = InstallDir, UseShellExecute = true });
}
