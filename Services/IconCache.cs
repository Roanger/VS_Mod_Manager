using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace VSModManager.Services;

/// <summary>
/// Loads mod icons: the mod's own modicon.png first, otherwise the ModDB logo,
/// which is cached on disk in %LocalAppData%\VSModManager\icons.
/// </summary>
public sealed class IconCache(ModDbClient client)
{
    private const int IconSize = 96; // decoded size; shown at 48px, sharp on 2x displays
    private readonly SemaphoreSlim _downloads = new(6);

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSModManager", "icons");

    public Task<Bitmap?> FromLocalAsync(string modPath) => Task.Run(() =>
        ModScanner.ReadIcon(modPath) is { } bytes ? Decode(bytes) : null);

    public async Task<Bitmap?> FromUrlAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var file = Path.Combine(CacheDir, Hash(url) + Path.GetExtension(new Uri(url).AbsolutePath));
        try
        {
            if (!File.Exists(file))
            {
                await _downloads.WaitAsync();
                try
                {
                    if (!File.Exists(file))
                    {
                        var bytes = await client.GetBytesAsync(url);
                        Directory.CreateDirectory(CacheDir);
                        await File.WriteAllBytesAsync(file, bytes);
                    }
                }
                finally
                {
                    _downloads.Release();
                }
            }
            return await Task.Run(() => Decode(File.ReadAllBytes(file)));
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, IconSize, BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            return null; // not an image Skia can read
        }
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();
}
