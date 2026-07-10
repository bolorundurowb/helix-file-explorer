using MomentNet.Display;

namespace HelixExplorer.Core.Formatting;

public static class RelativeTimeFormatter
{
    public static string Format(DateTime utc, DateTime? nowUtc = null)
    {
        if (utc == DateTime.MinValue)
            return string.Empty;

        return nowUtc.HasValue ? utc.From(nowUtc.Value) : utc.FromNow();
    }
}
