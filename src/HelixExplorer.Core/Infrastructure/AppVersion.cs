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
