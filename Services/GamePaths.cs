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

    public GamePaths(string? installDir = null, string? dataDir = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        InstallDir = installDir ?? Path.Combine(appData, "Vintagestory");
        DataDir = dataDir ?? Path.Combine(appData, "VintagestoryData");
        GameVersion = DetectGameVersion(InstallDir);
    }

    private static SemVer? DetectGameVersion(string installDir)
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
