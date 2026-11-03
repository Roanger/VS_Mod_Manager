using System.Text.RegularExpressions;

namespace VSModManager.Services;

/// <summary>
/// Lenient semantic version, e.g. "1.22.0", "4.4.1", "1.22.0-rc.10", "v3.0.0".
/// Prerelease segments compare numerically where possible so rc.10 > rc.9.
/// </summary>
public sealed partial class SemVer : IComparable<SemVer>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string[] Prerelease { get; }
    public string Original { get; }

    public bool IsPrerelease => Prerelease.Length > 0;

    private SemVer(int major, int minor, int patch, string[] pre, string original)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = pre;
        Original = original;
    }

    [GeneratedRegex(@"^\s*v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.\d+)*(?:-([0-9A-Za-z.\-]+))?(?:\+.*)?\s*$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out SemVer version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Pattern().Match(text);
        if (!m.Success) return false;

        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        var pre = m.Groups[4].Success ? m.Groups[4].Value.Split('.', StringSplitOptions.RemoveEmptyEntries) : [];
        version = new SemVer(Part(1), Part(2), Part(3), pre, text.Trim());
        return true;
    }

    public static SemVer? Parse(string? text) => TryParse(text, out var v) ? v : null;

    public bool SameMinor(SemVer other) => Major == other.Major && Minor == other.Minor;

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;

        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A release ranks above any of its prereleases.
        if (!IsPrerelease && other.IsPrerelease) return 1;
        if (IsPrerelease && !other.IsPrerelease) return -1;

        for (int i = 0; i < Math.Min(Prerelease.Length, other.Prerelease.Length); i++)
        {
            var a = Prerelease[i];
            var b = other.Prerelease[i];
            bool aNum = int.TryParse(a, out var an);
            bool bNum = int.TryParse(b, out var bn);

            c = (aNum, bNum) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (c != 0) return c;
        }

        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    public override string ToString() => Original;
}
