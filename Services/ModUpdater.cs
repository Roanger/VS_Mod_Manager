using System.IO.Compression;

namespace VSModManager.Services;

/// <summary>
/// Installs new releases and retires old files. Old files are never deleted,
/// only moved into a timestamped folder under ModManagerBackups.
/// </summary>
public sealed class ModUpdater(GamePaths paths, ModDbClient client)
{
    private readonly string _sessionBackupDir =
        Path.Combine(paths.BackupDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

    public async Task<string> InstallAsync(ModDbRelease release, IEnumerable<InstalledMod> replaced, CancellationToken ct = default)
    {
        if (GamePaths.IsGameRunning())
            throw new InvalidOperationException("Close Vintage Story before updating mods.");

        var fileName = SafeFileName(release.FileName ?? $"{release.ModIdStr}_{release.ModVersion}.zip");
        var tempPath = Path.Combine(Path.GetTempPath(), $"vsmm_{Guid.NewGuid():N}_{fileName}");

        try
        {
            await client.DownloadAsync(release.MainFile!, tempPath, ct);
            ValidateZip(tempPath);

            foreach (var old in replaced)
                Backup(old.Path);

            var target = Path.Combine(paths.ModsDir, fileName);
            if (File.Exists(target)) Backup(target);
            File.Move(tempPath, target);
            return target;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public void Backup(string path)
    {
        Directory.CreateDirectory(_sessionBackupDir);
        var dest = Path.Combine(_sessionBackupDir, Path.GetFileName(path));
        if (Directory.Exists(path))
            Directory.Move(path, dest);
        else if (File.Exists(path))
            File.Move(path, dest, overwrite: true);
    }

    private static void ValidateZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (!zip.Entries.Any(e => e.FullName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Downloaded file has no modinfo.json; not installing it.");
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
