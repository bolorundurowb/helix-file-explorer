using System.Reflection;

namespace HelixExplorer.Core.Infrastructure;

public static class AppVersion
{
    private static readonly Lazy<string> CurrentLazy = new(Resolve);

    /// <summary>
    /// Informational version without a Source Link / commit suffix (e.g. <c>0.2.1</c>).
    /// </summary>
    public static string Current => CurrentLazy.Value;

    /// <summary>
    /// Version string safe for use in file and directory names.
    /// </summary>
    public static string CurrentForPath => SanitizeForPath(Current);

    /// <summary>
    /// True when the running build's version carries a <c>-preview.N</c> suffix. Release publishes
    /// ship both stable and preview installs from the same build configuration, so this is how a
    /// preview install tells itself apart at runtime (e.g. to show the dev icon) without a separate
    /// configuration; <see cref="AppPaths.IsDevelopmentProfile"/> is unrelated and keeps preview
    /// installs on the same AppData/logs location as stable.
    /// </summary>
    public static bool IsPreview => IsPreviewVersion(Current);

    internal static bool IsPreviewVersion(string version)
        => HelixVersion.TryParse(version, out var parsed) && !parsed.IsStable;

    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    internal static string SanitizeForPath(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = version.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]) || chars[i] is '/' or '\\')
                chars[i] = '_';
        }

        return new string(chars);
    }
}
