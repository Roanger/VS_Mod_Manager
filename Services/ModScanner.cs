using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VSModManager.Services;

/// <summary>A single mod file or folder found in the Mods directory.</summary>
public sealed record InstalledMod(
    string Path,
    string? ModId,
    string Name,
    string? Version,
    IReadOnlyDictionary<string, string> Dependencies,
    string? Description = null)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public SemVer? SemVersion => SemVer.Parse(Version);
}

public static class ModScanner
{
    public static List<InstalledMod> Scan(string modsDir)
    {
        var result = new List<InstalledMod>();
        if (!Directory.Exists(modsDir)) return result;

        foreach (var file in Directory.EnumerateFiles(modsDir))
        {
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".zip")
                result.Add(ReadZip(file));
            else if (ext is ".dll" or ".cs")
                result.Add(Unknown(file));
        }

        foreach (var dir in Directory.EnumerateDirectories(modsDir))
        {
            var info = System.IO.Path.Combine(dir, "modinfo.json");
            if (File.Exists(info))
                result.Add(Parse(dir, File.ReadAllText(info)));
        }

        return result;
    }

    public static InstalledMod ReadZip(string file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return Unknown(file);

            using var reader = new StreamReader(entry.Open());
            return Parse(file, reader.ReadToEnd());
        }
        catch
        {
            return Unknown(file);
        }
    }

    private static InstalledMod Parse(string path, string json)
    {
        try
        {
            // modinfo.json is often hand-written: Newtonsoft tolerates comments,
            // trailing commas, unquoted keys and mixed-case property names.
            var obj = JObject.Parse(json, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
            string? Get(string key) =>
                obj.GetValue(key, StringComparison.OrdinalIgnoreCase)?.ToString();

            var deps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (obj.GetValue("dependencies", StringComparison.OrdinalIgnoreCase) is JObject d)
                foreach (var p in d.Properties())
                    deps[p.Name] = p.Value.ToString();

            var modId = Get("modid")?.ToLowerInvariant();
            return new InstalledMod(path, modId, Get("name") ?? modId ?? System.IO.Path.GetFileName(path), Get("version"), deps, Get("description"));
        }
        catch
        {
            return Unknown(path);
        }
    }

    /// <summary>Bytes of the mod's bundled modicon.png, if it has one.</summary>
    public static byte[]? ReadIcon(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var file = System.IO.Path.Combine(path, "modicon.png");
                return File.Exists(file) ? File.ReadAllBytes(file) : null;
            }

            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals("modicon.png", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            using var ms = new MemoryStream();
            using (var s = entry.Open()) s.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static InstalledMod Unknown(string path) =>
        new(path, null, System.IO.Path.GetFileName(path), null, new Dictionary<string, string>());
}
