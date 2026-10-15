using System.Text.RegularExpressions;

namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Parses and compares the <c>MAJOR.MINOR.PATCH[-preview.N]</c> versions this project
/// publishes, mirroring the rules in <c>scripts/Resolve-ReleaseVersion.ps1</c>. Used by the
/// in-app update checker instead of <see cref="Version.TryParse(string?, out Version?)"/>,
/// which cannot parse a SemVer prerelease suffix and would otherwise always report a
/// preview build as "no update available" (it fails to parse the current version at all).
/// </summary>
public readonly struct HelixVersion : IComparable<HelixVersion>, IEquatable<HelixVersion>
{
    // Leading zeros are rejected on purpose, matching the release workflow's own
    // validation: this project has never published "01.4.3", so silently coercing one
    // would hide a typo rather than surface it.
    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.([1-9]\d*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Null for a stable release.</summary>
    public int? Preview { get; }

    public bool IsStable => Preview is null;

    private HelixVersion(int major, int minor, int patch, int? preview)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Preview = preview;
    }

    public static bool TryParse(string? value, out HelixVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim().TrimStart('v', 'V');
        var match = Pattern.Match(trimmed);
        if (!match.Success)
            return false;

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = int.Parse(match.Groups[3].Value);
        var preview = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : (int?)null;

        version = new HelixVersion(major, minor, patch, preview);
        return true;
    }

    /// <summary>
    /// A stable release always outranks a preview on the same MAJOR.MINOR.PATCH base,
    /// since shipping stable supersedes that preview.
    /// </summary>
    public int CompareTo(HelixVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0) return patch;

        if (IsStable && other.IsStable) return 0;
        if (IsStable) return 1;
        if (other.IsStable) return -1;

        return Preview!.Value.CompareTo(other.Preview!.Value);
    }

    public bool Equals(HelixVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is HelixVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Preview);

    public static bool operator >(HelixVersion left, HelixVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(HelixVersion left, HelixVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(HelixVersion left, HelixVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(HelixVersion left, HelixVersion right) => left.CompareTo(right) <= 0;

    public static bool operator ==(HelixVersion left, HelixVersion right) => left.Equals(right);

    public static bool operator !=(HelixVersion left, HelixVersion right) => !left.Equals(right);

    public override string ToString() => IsStable
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}-preview.{Preview}";
}
