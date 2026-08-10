using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Grouping;

/// <summary>
/// Pure bucket assignment for Explorer-style Group By. Every bucket for the fixed modes is a
/// cached static, so <see cref="GetBucket"/> allocates nothing except for <see cref="GroupByMode.Type"/>,
/// where the label is derived from the entry's extension. Callers that group large listings should
/// cache Type buckets per extension rather than calling this per comparison.
/// </summary>
public static class FileGrouping
{
    public static readonly FileGroupBucket Ungrouped = new("none", string.Empty, 0);

    public static readonly FileGroupBucket NameOther = new("name_other", "#", 0);
    public static readonly FileGroupBucket NameAToH = new("name_a_h", "A–H", 1);
    public static readonly FileGroupBucket NameIToP = new("name_i_p", "I–P", 2);
    public static readonly FileGroupBucket NameQToZ = new("name_q_z", "Q–Z", 3);

    public static readonly FileGroupBucket ModifiedToday = new("modified_today", "Today", 0);
    public static readonly FileGroupBucket ModifiedYesterday = new("modified_yesterday", "Yesterday", 1);
    public static readonly FileGroupBucket ModifiedEarlierThisWeek = new("modified_earlier_this_week", "Earlier this week", 2);
    public static readonly FileGroupBucket ModifiedLastWeek = new("modified_last_week", "Last week", 3);
    public static readonly FileGroupBucket ModifiedEarlierThisMonth = new("modified_earlier_this_month", "Earlier this month", 4);
    public static readonly FileGroupBucket ModifiedLastMonth = new("modified_last_month", "Last month", 5);
    public static readonly FileGroupBucket ModifiedLongAgo = new("modified_long_ago", "A long time ago", 6);

    /// <summary>Folders always lead in Type and Size grouping, mirroring Explorer.</summary>
    public static readonly FileGroupBucket TypeFolders = new("type_folder", "Folders", 0);

    public static readonly FileGroupBucket TypeFile = new("type_file", "File", 1);

    public static readonly FileGroupBucket SizeFolders = new("size_folder", "Folders", 0);
    public static readonly FileGroupBucket SizeTiny = new("size_tiny", "Tiny", 1);
    public static readonly FileGroupBucket SizeSmall = new("size_small", "Small", 2);
    public static readonly FileGroupBucket SizeMedium = new("size_medium", "Medium", 3);
    public static readonly FileGroupBucket SizeLarge = new("size_large", "Large", 4);
    public static readonly FileGroupBucket SizeHuge = new("size_huge", "Huge", 5);
    public static readonly FileGroupBucket SizeGigantic = new("size_gigantic", "Gigantic", 6);

    private const long Kilobyte = 1024L;
    private const long Megabyte = 1024L * 1024L;
    private const long Gigabyte = 1024L * 1024L * 1024L;

    public const long TinyMaxBytes = 16 * Kilobyte;
    public const long SmallMaxBytes = Megabyte;
    public const long MediumMaxBytes = 128 * Megabyte;
    public const long LargeMaxBytes = Gigabyte;
    public const long HugeMaxBytes = 4 * Gigabyte;

    public static FileGroupBucket GetBucket(in FileSystemEntry entry, GroupByMode mode, DateTime utcNow) => mode switch
    {
        GroupByMode.Name => GetNameBucket(entry.Name),
        GroupByMode.Modified => GetModifiedBucket(entry.ModifiedUtc, utcNow),
        GroupByMode.Type => GetTypeBucket(in entry),
        GroupByMode.Size => GetSizeBucket(entry.IsDirectory, entry.SizeBytes),
        _ => Ungrouped
    };

    public static FileGroupBucket GetNameBucket(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return NameOther;

        var first = char.ToUpperInvariant(name[0]);
        return first switch
        {
            >= 'A' and <= 'H' => NameAToH,
            >= 'I' and <= 'P' => NameIToP,
            >= 'Q' and <= 'Z' => NameQToZ,
            _ => NameOther
        };
    }

    /// <summary>
    /// Boundaries are evaluated entirely in UTC so the same inputs always produce the same bucket.
    /// Weeks start on Monday; "Yesterday" wins over the week buckets when the two overlap.
    /// </summary>
    public static FileGroupBucket GetModifiedBucket(DateTime modifiedUtc, DateTime utcNow)
    {
        var today = utcNow.Date;
        var day = modifiedUtc.Date;

        // Clock skew and files stamped in the future read as Today rather than falling off the scale.
        if (day >= today)
            return ModifiedToday;

        if (day == today.AddDays(-1))
            return ModifiedYesterday;

        var weekStart = today.AddDays(-(((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
        if (day >= weekStart)
            return ModifiedEarlierThisWeek;

        if (day >= weekStart.AddDays(-7))
            return ModifiedLastWeek;

        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, today.Kind);
        if (day >= monthStart)
            return ModifiedEarlierThisMonth;

        if (day >= monthStart.AddMonths(-1))
            return ModifiedLastMonth;

        return ModifiedLongAgo;
    }

    public static FileGroupBucket GetTypeBucket(in FileSystemEntry entry)
    {
        if (entry.IsDirectory)
            return TypeFolders;

        if (string.IsNullOrEmpty(entry.Extension))
            return TypeFile;

        var label = entry.TypeLabel;
        return new FileGroupBucket("type_" + entry.Extension.TrimStart('.').ToLowerInvariant(), label, 1);
    }

    public static FileGroupBucket GetSizeBucket(bool isDirectory, long sizeBytes)
    {
        if (isDirectory)
            return SizeFolders;

        return sizeBytes switch
        {
            < TinyMaxBytes => SizeTiny,
            < SmallMaxBytes => SizeSmall,
            < MediumMaxBytes => SizeMedium,
            < LargeMaxBytes => SizeLarge,
            < HugeMaxBytes => SizeHuge,
            _ => SizeGigantic
        };
    }

    /// <summary>
    /// Buckets order by <see cref="FileGroupBucket.Order"/> first. Type grouping shares one order
    /// across every file extension, so the display name breaks that tie alphabetically.
    /// </summary>
    public static int CompareBuckets(in FileGroupBucket a, in FileGroupBucket b)
    {
        var cmp = a.Order.CompareTo(b.Order);
        if (cmp != 0)
            return cmp;

        cmp = StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);
        return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.Key, b.Key);
    }
}
