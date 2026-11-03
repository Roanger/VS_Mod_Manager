using System.Diagnostics;

namespace VSModManager.Services;

/// <summary>Locates the Vintage Story install, data folder and game version.</summary>
public sealed class GamePaths
{
    /// <summary>Flatpak app id of the Vintage Story package on Flathub.</summary>
    public const string FlatpakId = "at.vintagestory.VintageStory";

    public string InstallDir { get; }
    public string DataDir { get; }
    public string ModsDir => Path.Combine(DataDir, "Mods");
    public string BackupDir => Path.Combine(DataDir, "ModManagerBackups");
    public SemVer? GameVersion { get; }

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string FlatpakDataDir =>
        Path.Combine(Home, ".var", "app", FlatpakId, "config", "VintagestoryData");

    private static string? _defaultInstallDir;

    /// <summary>
    /// Windows: %AppData%\Vintagestory. Linux: the first folder that holds the game,
    /// from .desktop launchers, common install spots, and Flatpak installs.
    /// </summary>
    public static string DefaultInstallDir => _defaultInstallDir ??= OperatingSystem.IsWindows()
        ? Path.Combine(AppData, "Vintagestory")
        : FindLinuxInstallDir();

    /// <summary>
    /// Windows: %AppData%\VintagestoryData. Linux: ~/.config/VintagestoryData, or the
    /// Flatpak's data folder when only that one exists.
    /// </summary>
    public static string DefaultDataDir
    {
        get
        {
            var native = Path.Combine(AppData, "VintagestoryData"); // ~/.config on Linux
            if (!OperatingSystem.IsWindows() && !Directory.Exists(native) && Directory.Exists(FlatpakDataDir))
                return FlatpakDataDir;
            return native;
        }
    }

    public GamePaths(string? installDir = null, string? dataDir = null)
    {
        InstallDir = installDir ?? DefaultInstallDir;
        DataDir = dataDir ?? DefaultDataDir;
        GameVersion = DetectGameVersion(InstallDir);
    }

    public GamePaths(AppSettings settings) : this(settings.InstallDir, settings.DataDir) { }

    /// <summary>True if the folder holds the game (Vintagestory.exe on Windows, Vintagestory.dll anywhere).</summary>
    public static bool IsGameFolder(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) &&
        (File.Exists(Path.Combine(dir, "Vintagestory.dll")) || File.Exists(Path.Combine(dir, "Vintagestory.exe")));

    /// <summary>Returns an error message, or null if the folder holds the game.</summary>
    public static string? ValidateInstallDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "Folder doesn't exist.";
        if (!IsGameFolder(dir)) return "No Vintage Story game files (Vintagestory.dll) in this folder.";
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
        // The exe carries the version on Windows; the dll carries it everywhere
        // (FileVersionInfo can read managed assemblies on Linux too).
        foreach (var name in new[] { "Vintagestory.exe", "Vintagestory.dll" })
        {
            var file = Path.Combine(installDir, name);
            if (!File.Exists(file)) continue;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(file);
                if ((SemVer.Parse(info.ProductVersion) ?? SemVer.Parse(info.FileVersion)) is { } v)
                    return v;
            }
            catch
            {
                // Unreadable; try the next file.
            }
        }
        return null;
    }

    /// <summary>True when the game was installed from Flathub.</summary>
    public bool IsFlatpak =>
        !OperatingSystem.IsWindows() &&
        (DataDir.StartsWith(Path.Combine(Home, ".var", "app", FlatpakId), StringComparison.Ordinal) ||
         InstallDir.Contains($"/flatpak/app/{FlatpakId}/", StringComparison.Ordinal));

    /// <summary>The native game executable, or null if it can't be found.</summary>
    private string? Executable
    {
        get
        {
            var exe = Path.Combine(InstallDir, OperatingSystem.IsWindows() ? "Vintagestory.exe" : "Vintagestory");
            return File.Exists(exe) ? exe : null;
        }
    }

    public bool CanLaunch =>
        IsFlatpak || Executable is not null || File.Exists(Path.Combine(InstallDir, "Vintagestory.dll"));

    public static bool IsGameRunning()
    {
        if (AnyProcess("Vintagestory")) return true;
        if (OperatingSystem.IsWindows()) return false;

        // Some Linux launchers run "dotnet Vintagestory.dll" instead of the native binary.
        foreach (var p in Process.GetProcessesByName("dotnet"))
        {
            using (p)
            {
                try
                {
                    var cmdline = File.ReadAllText($"/proc/{p.Id}/cmdline");
                    if (cmdline.Contains("Vintagestory.dll", StringComparison.Ordinal)) return true;
                }
                catch
                {
                    // Process exited or isn't readable.
                }
            }
        }
        return false;
    }

    private static bool AnyProcess(string name)
    {
        var found = Process.GetProcessesByName(name);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    public void LaunchGame()
    {
        if (IsFlatpak)
        {
            Process.Start(new ProcessStartInfo("flatpak", ["run", FlatpakId]) { UseShellExecute = false });
            return;
        }

        var start = Executable is { } exe
            ? new ProcessStartInfo(exe)
            : new ProcessStartInfo("dotnet", [Path.Combine(InstallDir, "Vintagestory.dll")]);
        start.WorkingDirectory = InstallDir;
        // Shell-execute on Linux means xdg-open, which opens files rather than running them.
        start.UseShellExecute = OperatingSystem.IsWindows();
        Process.Start(start);
    }

    // ---- Linux install discovery ----

    private static string FindLinuxInstallDir()
    {
        var candidates = DesktopLauncherDirs()
            .Concat(
            [
                "/opt/vintagestory",
                "/usr/share/vintagestory",
                "/usr/lib/vintagestory",
                Path.Combine(Home, ".local", "share", "vintagestory"),
                Path.Combine(Home, "ApplicationData", "vintagestory"),
                Path.Combine(Home, "vintagestory"),
                Path.Combine(Home, "Games", "vintagestory"),
                Path.Combine(Home, "Games", "VintageStory"),
            ])
            .Concat(FlatpakInstallDirs());

        foreach (var dir in candidates)
            if (IsGameFolder(dir)) return dir;

        return "/opt/vintagestory";
    }

    /// <summary>Folders named by Vintage Story .desktop files (Path= or the Exec binary's folder).</summary>
    private static IEnumerable<string> DesktopLauncherDirs()
    {
        string[] appDirs =
        [
            Path.Combine(Home, ".local", "share", "applications"),
            "/usr/local/share/applications",
            "/usr/share/applications",
        ];

        foreach (var appDir in appDirs)
        {
            if (!Directory.Exists(appDir)) continue;

            string[] files;
            try { files = Directory.GetFiles(appDir, "*.desktop"); }
            catch { continue; }

            foreach (var file in files.Where(f => Path.GetFileName(f).Contains("vintage", StringComparison.OrdinalIgnoreCase)))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                foreach (var line in lines)
                {
                    if (line.StartsWith("Path=", StringComparison.Ordinal))
                        yield return line[5..].Trim();
                    else if (line.StartsWith("Exec=", StringComparison.Ordinal))
                    {
                        var cmd = line[5..].Trim();
                        var exe = cmd.StartsWith('"') ? cmd[1..].Split('"')[0] : cmd.Split(' ')[0];
                        if (Path.GetDirectoryName(exe) is { Length: > 0 } dir) yield return dir;
                    }
                }
            }
        }
    }

    /// <summary>The game folder inside a system or user Flatpak install, if any.</summary>
    private static IEnumerable<string> FlatpakInstallDirs()
    {
        string[] roots =
        [
            Path.Combine(Home, ".local", "share", "flatpak", "app", FlatpakId, "current", "active", "files"),
            $"/var/lib/flatpak/app/{FlatpakId}/current/active/files",
        ];

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            string? found = null;
            try
            {
                found = Directory.EnumerateFiles(root, "Vintagestory.dll",
                        new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                    .FirstOrDefault();
            }
            catch
            {
                // Unreadable; skip.
            }
            if (found is not null) yield return Path.GetDirectoryName(found)!;
        }
    }
}
