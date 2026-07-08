namespace HelixExplorer.Core.Search;

/// <summary>Subsequence fuzzy matching for command palette ranking.</summary>
public static class FuzzyMatcher
{
    public static int Score(string target, string query)
    {
        if (string.IsNullOrEmpty(query))
            return 0;
        if (string.IsNullOrEmpty(target))
            return -1;

        var score = 0;
        var ti = 0;
        var consecutive = 0;
        var prevWasSeparator = true;

        for (var qi = 0; qi < query.Length; qi++)
        {
            var qc = char.ToLowerInvariant(query[qi]);
            var matched = false;
            while (ti < target.Length)
            {
                var tc = target[ti];
                var boundary = prevWasSeparator;
                prevWasSeparator = tc is ' ' or '/' or '\\' or '_' or '-' or '.';
                if (char.ToLowerInvariant(tc) == qc)
                {
                    score += 1 + consecutive * 2 + (boundary ? 3 : 0);
                    consecutive++;
                    ti++;
                    matched = true;
                    break;
                }

                consecutive = 0;
                ti++;
            }

            if (!matched)
                return -1;
        }

        return score;
    }
}
