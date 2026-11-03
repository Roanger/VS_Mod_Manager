using Newtonsoft.Json;

namespace VSModManager.Services;

/// <summary>
/// The manager's own settings, in %AppData%\VSModManager\settings.json.
/// A null path means "use the default location".
/// </summary>
public sealed class AppSettings
{
    public string? InstallDir { get; set; }
    public string? DataDir { get; set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VSModManager", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // Unreadable settings just fall back to defaults.
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
