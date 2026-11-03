using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VSModManager.Services;

/// <summary>
/// Reads and writes the game's own disabled-mod list in clientsettings.json
/// (stringListSettings.disabledMods, entries like "healthbar@1.4.2"), so the
/// manager and the in-game mod screen always agree.
/// </summary>
public sealed class ClientSettings(GamePaths paths)
{
    private bool _backedUp;

    public string FilePath => Path.Combine(paths.DataDir, "clientsettings.json");

    public static string Key(InstalledMod mod) => $"{mod.ModId}@{mod.Version}";

    public HashSet<string> LoadDisabled()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath)) return set;

        if (Load()["stringListSettings"]?["disabledMods"] is JArray arr)
            foreach (var v in arr)
                set.Add(v.ToString());
        return set;
    }

    public static bool IsDisabled(InstalledMod mod, HashSet<string> disabled) =>
        mod.ModId is not null && (disabled.Contains(Key(mod)) || disabled.Contains(mod.ModId));

    /// <summary>
    /// Applies a change to the disabled list. Always re-reads the file first so
    /// nothing else in it is clobbered.
    /// </summary>
    public void Edit(Action<HashSet<string>> change)
    {
        if (GamePaths.IsGameRunning())
            throw new InvalidOperationException("Close Vintage Story first; it overwrites clientsettings.json on exit.");

        var root = File.Exists(FilePath) ? Load() : new JObject();
        // Only attach when missing: re-assigning a token that already has a parent
        // makes Newtonsoft clone it, and our edits would land on a detached copy.
        if (root["stringListSettings"] is not JObject lists)
            root["stringListSettings"] = lists = new JObject();

        var disabled = new HashSet<string>(
            (lists["disabledMods"] as JArray)?.Select(v => v.ToString()) ?? [],
            StringComparer.OrdinalIgnoreCase);
        change(disabled);
        lists["disabledMods"] = new JArray(disabled.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

        Save(root);
    }

    /// <param name="copies">Every installed copy of the mod, newest first.</param>
    /// <remarks>Disabling covers all copies so the game can't fall back to an older
    /// duplicate; enabling only turns on the newest.</remarks>
    public void SetDisabled(IReadOnlyList<InstalledMod> copies, bool disabled) => Edit(set =>
    {
        set.Remove(copies[0].ModId!);
        if (disabled)
            foreach (var c in copies) set.Add(Key(c));
        else
            set.Remove(Key(copies[0]));
    });

    /// <summary>
    /// After an update or duplicate cleanup: drop entries for files that are gone,
    /// and carry a disabled flag over to the version that replaced them.
    /// </summary>
    public void Replace(IEnumerable<InstalledMod> removed, InstalledMod? replacement, bool keepDisabled) => Edit(set =>
    {
        foreach (var old in removed)
            set.Remove(Key(old));
        if (replacement is not null && keepDisabled)
            set.Add(Key(replacement));
    });

    private JObject Load()
    {
        using var reader = new JsonTextReader(new StreamReader(FilePath))
        {
            // Keep strings and numbers exactly as written.
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
        };
        return JObject.Load(reader);
    }

    private void Save(JObject root)
    {
        if (!_backedUp && File.Exists(FilePath))
        {
            Directory.CreateDirectory(paths.BackupDir);
            File.Copy(FilePath, Path.Combine(paths.BackupDir, "clientsettings.json.bak"), overwrite: true);
            _backedUp = true;
        }

        var temp = FilePath + ".vsmm.tmp";
        File.WriteAllText(temp, root.ToString(Formatting.Indented));
        File.Move(temp, FilePath, overwrite: true);
    }
}
