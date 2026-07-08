namespace HelixExplorer.Core.Formatting;

public static class RelativeTimeFormatter
{
    public static string Format(DateTime utc, DateTime? nowUtc = null)
    {
        if (utc == DateTime.MinValue)
            return string.Empty;

        var now = nowUtc ?? DateTime.UtcNow;
        var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var delta = now - (utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime());

        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta < TimeSpan.FromMinutes(1))
            return "Just now";
        if (delta < TimeSpan.FromHours(1))
        {
            var mins = Math.Max(1, (int)delta.TotalMinutes);
            return mins == 1 ? "1 minute ago" : $"{mins} minutes ago";
        }
        if (delta < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)delta.TotalHours);
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        if (delta < TimeSpan.FromDays(7))
        {
            var days = Math.Max(1, (int)delta.TotalDays);
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        return local.ToString("g");
    }
}
