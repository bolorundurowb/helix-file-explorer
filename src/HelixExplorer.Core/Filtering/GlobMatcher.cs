namespace HelixExplorer.Core.Filtering;

/// <summary>
/// Case-insensitive glob matching for <c>*</c>, <c>?</c>, and simple character classes.
/// Also supports <c>**</c> as "match across path segments" when matching relative paths.
/// </summary>
public static class GlobMatcher
{
    public static bool HasGlobMetacharacters(ReadOnlySpan<char> pattern)
    {
        pattern = pattern.Trim();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c is '*' or '?' or '[')
                return true;
        }

        return false;
    }

    public static bool IsMatch(string text, string pattern)
        => IsMatch(text.AsSpan(), pattern.AsSpan());

    public static bool IsMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        pattern = pattern.Trim();
        if (pattern.IsEmpty)
            return true;

        return Match(text, pattern);
    }

    private static bool Match(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        var ti = 0;
        var pi = 0;
        var starText = -1;
        var starPattern = -1;

        while (ti < text.Length)
        {
            if (pi < pattern.Length)
            {
                var pc = pattern[pi];

                if (pc == '*')
                {
                    // Collapse "**" / consecutive stars.
                    while (pi < pattern.Length && pattern[pi] == '*')
                        pi++;

                    starPattern = pi;
                    starText = ti;
                    continue;
                }

                if (pc == '?')
                {
                    ti++;
                    pi++;
                    continue;
                }

                if (pc == '[')
                {
                    if (!TryMatchClass(text[ti], pattern, ref pi))
                        goto starBacktrack;
                    ti++;
                    continue;
                }

                if (CharsEqual(text[ti], pc))
                {
                    ti++;
                    pi++;
                    continue;
                }
            }

        starBacktrack:
            if (starPattern < 0)
                return false;

            starText++;
            ti = starText;
            pi = starPattern;
        }

        while (pi < pattern.Length && pattern[pi] == '*')
            pi++;

        return pi == pattern.Length;
    }

    private static bool TryMatchClass(char c, ReadOnlySpan<char> pattern, ref int pi)
    {
        // pattern[pi] == '['
        pi++;
        if (pi >= pattern.Length)
            return false;

        var negate = pattern[pi] == '!';
        if (negate)
            pi++;

        var matched = false;
        var closed = false;
        while (pi < pattern.Length)
        {
            var pc = pattern[pi];
            if (pc == ']')
            {
                pi++;
                closed = true;
                break;
            }

            if (pi + 2 < pattern.Length && pattern[pi + 1] == '-')
            {
                var start = pattern[pi];
                var end = pattern[pi + 2];
                if (CharsInRange(c, start, end))
                    matched = true;
                pi += 3;
                continue;
            }

            if (CharsEqual(c, pc))
                matched = true;
            pi++;
        }

        if (!closed)
            return false;

        return negate ? !matched : matched;
    }

    private static bool CharsEqual(char a, char b)
        => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    private static bool CharsInRange(char c, char start, char end)
    {
        var value = char.ToLowerInvariant(c);
        var lo = char.ToLowerInvariant(start);
        var hi = char.ToLowerInvariant(end);
        if (lo > hi)
            (lo, hi) = (hi, lo);
        return value >= lo && value <= hi;
    }
}
