namespace VSModManager.Services;

public enum DependencyProblem
{
    Missing,
    TooOld,
    GameTooOld,
}

public sealed record DependencyIssue(
    string ModId,
    string? RequiredVersion,
    string? InstalledVersion,
    DependencyProblem Problem)
{
    public string Describe()
    {
        var req = string.IsNullOrEmpty(RequiredVersion) ? "" : $" ≥ {RequiredVersion}";
        return Problem switch
        {
            DependencyProblem.Missing => $"Missing {ModId}{req}",
            DependencyProblem.TooOld => $"Needs {ModId}{req} (have {InstalledVersion})",
            DependencyProblem.GameTooOld => $"Needs game{req} (have {InstalledVersion})",
            _ => ModId,
        };
    }
}

/// <summary>
/// Mirrors how the game resolves modinfo.json dependencies: each entry is
/// modid → minimum version, where an empty string or "*" means any version.
/// </summary>
public static class DependencyChecker
{
    // Base game mods ship with the game, so their version is the game version.
    private static readonly HashSet<string> BuiltIn =
        new(StringComparer.OrdinalIgnoreCase) { "game", "survival", "creative", "essentials" };

    /// <param name="installed">Highest installed version per modid.</param>
    public static List<DependencyIssue> Check(
        InstalledMod mod,
        IReadOnlyDictionary<string, InstalledMod> installed,
        SemVer? gameVersion)
    {
        var issues = new List<DependencyIssue>();

        foreach (var (depId, versionText) in mod.Dependencies)
        {
            var required = versionText is "" or "*" ? null : SemVer.Parse(versionText);

            if (BuiltIn.Contains(depId))
            {
                if (required is not null && gameVersion is not null && gameVersion.CompareTo(required) < 0)
                    issues.Add(new(depId, versionText, gameVersion.ToString(), DependencyProblem.GameTooOld));
                continue;
            }

            if (!installed.TryGetValue(depId, out var dep))
            {
                issues.Add(new(depId.ToLowerInvariant(), versionText, null, DependencyProblem.Missing));
                continue;
            }

            if (required is not null && dep.SemVersion is { } have && have.CompareTo(required) < 0)
                issues.Add(new(dep.ModId!, versionText, dep.Version, DependencyProblem.TooOld));
        }

        return issues;
    }
}
