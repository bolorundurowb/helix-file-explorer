using HelixExplorer.Core.Settings;

namespace HelixExplorer.Core.Formatting;

public static class FileSizeFormatter
{
    public static string Format(long sizeBytes, SizeDisplayMode mode, bool isDirectory)
    {
        if (isDirectory || sizeBytes < 0)
            return string.Empty;

        return mode == SizeDisplayMode.Decimal
            ? FormatDecimal(sizeBytes)
            : FormatBinary(sizeBytes);
    }

    public static string FormatBinary(long size)
    {
        if (size < 1024) return FormattableString.Invariant($"{size} B");
        if (size < 1_048_576) return FormattableString.Invariant($"{size / 1024.0:F1} KiB");
        if (size < 1_073_741_824) return FormattableString.Invariant($"{size / 1_048_576.0:F1} MiB");
        if (size < 1_099_511_627_776L) return FormattableString.Invariant($"{size / 1_073_741_824.0:F2} GiB");
        return FormattableString.Invariant($"{size / 1_099_511_627_776.0:F2} TiB");
    }

    public static string FormatDecimal(long size)
    {
        if (size < 1000) return FormattableString.Invariant($"{size} B");
        if (size < 1_000_000) return FormattableString.Invariant($"{size / 1000.0:F1} KB");
        if (size < 1_000_000_000) return FormattableString.Invariant($"{size / 1_000_000.0:F1} MB");
        if (size < 1_000_000_000_000L) return FormattableString.Invariant($"{size / 1_000_000_000.0:F2} GB");
        return FormattableString.Invariant($"{size / 1_000_000_000_000.0:F2} TB");
    }
}
