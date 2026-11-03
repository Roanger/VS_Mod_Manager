using Newtonsoft.Json;

namespace VSModManager.Services;

public sealed class Profile
{
    public string Name { get; set; } = "";

    /// <summary>Modids (no version) to disable; everything else is enabled.</summary>
    public HashSet<string> DisabledMods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Profiles saved as disable-lists in %AppData%\VSModManager\profiles.json.</summary>
public sealed class ProfileStore
{
    public const string DefaultName = "Default";

    public List<Profile> Profiles { get; set; } = [];
    public string? Active { get; set; }

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VSModManager", "profiles.json");

    [JsonIgnore]
    public Profile? ActiveProfile => Find(Active);

    public Profile? Find(string? name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ProfileStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<ProfileStore>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // A corrupt file shouldn't stop the app; keep it for inspection and start over.
            File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
        File.Move(temp, FilePath, overwrite: true);
    }
}
