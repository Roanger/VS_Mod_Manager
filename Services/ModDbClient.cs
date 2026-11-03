using Newtonsoft.Json;

namespace VSModManager.Services;

public sealed class ModDbRelease
{
    [JsonProperty("releaseid")] public int ReleaseId { get; set; }
    [JsonProperty("mainfile")] public string? MainFile { get; set; }
    [JsonProperty("filename")] public string? FileName { get; set; }
    [JsonProperty("tags")] public List<string> Tags { get; set; } = [];
    [JsonProperty("modidstr")] public string? ModIdStr { get; set; }
    [JsonProperty("modversion")] public string? ModVersion { get; set; }
    [JsonProperty("created")] public string? Created { get; set; }
    [JsonProperty("changelog")] public string? Changelog { get; set; }

    public SemVer? Version => SemVer.Parse(ModVersion);
}

public sealed class ModDbMod
{
    [JsonProperty("assetid")] public int AssetId { get; set; }
    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("author")] public string? Author { get; set; }
    [JsonProperty("urlalias")] public string? UrlAlias { get; set; }
    [JsonProperty("releases")] public List<ModDbRelease> Releases { get; set; } = [];

    public string PageUrl => string.IsNullOrEmpty(UrlAlias)
        ? $"https://mods.vintagestory.at/show/mod/{AssetId}"
        : $"https://mods.vintagestory.at/{UrlAlias}";

    /// <summary>
    /// Highest-versioned release tagged for the game's major.minor (e.g. any 1.22.x).
    /// Tags rarely list every patch version, so an exact match would miss most mods.
    /// </summary>
    public ModDbRelease? LatestCompatible(SemVer? gameVersion) =>
        Releases
            .Where(r => r.Version is not null && r.MainFile is not null)
            .Where(r => gameVersion is null || r.Tags.Any(t => SemVer.Parse(t) is { } tv && tv.SameMinor(gameVersion)))
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
}

public sealed class ModDbClient : IDisposable
{
    private const string BaseUrl = "https://mods.vintagestory.at/api/";
    private readonly HttpClient _http;

    private sealed class ModResponse
    {
        [JsonProperty("statuscode")] public string? StatusCode { get; set; }
        [JsonProperty("mod")] public ModDbMod? Mod { get; set; }
    }

    public ModDbClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VSModManager/0.1");
    }

    /// <summary>Returns null when the mod isn't on the ModDB (API answers statuscode "404").</summary>
    public async Task<ModDbMod?> GetModAsync(string modId, CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(BaseUrl + "mod/" + Uri.EscapeDataString(modId), ct);
        var resp = JsonConvert.DeserializeObject<ModResponse>(json);
        return resp?.StatusCode == "200" ? resp.Mod : null;
    }

    public async Task DownloadAsync(string url, string destination, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destination);
        await src.CopyToAsync(dst, ct);
    }

    public void Dispose() => _http.Dispose();
}
