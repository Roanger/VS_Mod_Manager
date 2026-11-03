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
    [JsonProperty("logofile")] public string? LogoFile { get; set; }
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

/// <summary>One entry from /api/mods: lightweight info for every mod on the ModDB.</summary>
public sealed class ModDbListEntry
{
    [JsonProperty("modid")] public int ModId { get; set; }
    [JsonProperty("assetid")] public int AssetId { get; set; }
    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("summary")] public string? Summary { get; set; }
    [JsonProperty("author")] public string? Author { get; set; }
    [JsonProperty("logo")] public string? Logo { get; set; }
    [JsonProperty("downloads")] public int Downloads { get; set; }
    [JsonProperty("follows")] public int Follows { get; set; }
    [JsonProperty("trendingpoints")] public int TrendingPoints { get; set; }
    [JsonProperty("side")] public string? Side { get; set; }
    [JsonProperty("type")] public string? Type { get; set; }
    [JsonProperty("urlalias")] public string? UrlAlias { get; set; }
    [JsonProperty("lastreleased")] public string? LastReleased { get; set; }
    [JsonProperty("modidstrs")] public List<string> ModIdStrs { get; set; } = [];
    [JsonProperty("tags")] public List<string> Tags { get; set; } = [];
}

public sealed class ModDbClient : IDisposable
{
    private const string BaseUrl = "https://mods.vintagestory.at/api/";
    private readonly HttpClient _http;
    private Task<IReadOnlyList<ModDbListEntry>>? _modList;

    private sealed class ModResponse
    {
        [JsonProperty("statuscode")] public string? StatusCode { get; set; }
        [JsonProperty("mod")] public ModDbMod? Mod { get; set; }
    }

    private sealed class ModListResponse
    {
        [JsonProperty("mods")] public List<ModDbListEntry> Mods { get; set; } = [];
    }

    public ModDbClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VSModManager/0.2");
    }

    /// <summary>
    /// Every mod on the ModDB (~4 MB). Fetched once per session and shared;
    /// a failed fetch is forgotten so the next call retries.
    /// </summary>
    public Task<IReadOnlyList<ModDbListEntry>> GetModListAsync() => _modList ??= FetchModListAsync();

    private async Task<IReadOnlyList<ModDbListEntry>> FetchModListAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(BaseUrl + "mods");
            return JsonConvert.DeserializeObject<ModListResponse>(json)?.Mods ?? [];
        }
        catch
        {
            _modList = null;
            throw;
        }
    }

    public Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default) =>
        _http.GetByteArrayAsync(url, ct);

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
