using System.Text;

namespace HelixExplorer.Core.Git;

/// <summary>Parses <c>git status --porcelain=v2 --branch</c> output into an aggregate + file map.</summary>
public static class GitPorcelainParser
{
    public static GitStatusSnapshot Parse(string output, string? repoRoot)
    {
        if (string.IsNullOrEmpty(output))
            return new GitStatusSnapshot(GitStatus.Empty, repoRoot, new Dictionary<string, GitFileStatus>(0));

        var branch = string.Empty;
        var ahead = 0;
        var behind = 0;
        var staged = 0;
        var unstaged = 0;
        var untracked = 0;
        var hasUpstream = false;
        var files = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);

        var lines = output.AsSpan();
        while (!lines.IsEmpty)
        {
            var newline = lines.IndexOf('\n');
            ReadOnlySpan<char> line;
            if (newline < 0)
            {
                line = lines;
                lines = ReadOnlySpan<char>.Empty;
            }
            else
            {
                line = lines[..newline];
                lines = lines[(newline + 1)..];
            }

            if (line.Length > 0 && line[^1] == '\r')
                line = line[..^1];
            if (line.IsEmpty)
                continue;

            if (line[0] == '#')
            {
                ParseHeader(line, ref branch, ref ahead, ref behind, ref hasUpstream);
                continue;
            }

            switch (line[0])
            {
                case '?':
                    untracked++;
                    Upsert(files, ExtractUntrackedPath(line), GitFileStatus.Untracked);
                    break;
                case 'u':
                    Upsert(files, ExtractOrdinaryPath(line), GitFileStatus.Conflict);
                    CountXy(line, ref staged, ref unstaged);
                    break;
                case '1':
                case '2':
                    var fileStatus = ClassifyOrdinary(line);
                    Upsert(files, ExtractOrdinaryPath(line), fileStatus);
                    CountXy(line, ref staged, ref unstaged);
                    break;
            }
        }

        if (string.IsNullOrEmpty(branch))
            branch = "(detached)";

        var status = new GitStatus(branch, staged, unstaged, untracked, hasUpstream, ahead, behind);
        return new GitStatusSnapshot(status, repoRoot, files);
    }

    private static void ParseHeader(
        ReadOnlySpan<char> line,
        ref string branch,
        ref int ahead,
        ref int behind,
        ref bool hasUpstream)
    {
        const string Head = "# branch.head ";
        const string Upstream = "# branch.upstream ";
        const string Ab = "# branch.ab ";

        if (line.StartsWith(Head, StringComparison.Ordinal))
        {
            branch = line[Head.Length..].ToString();
            return;
        }

        if (line.StartsWith(Upstream, StringComparison.Ordinal))
        {
            hasUpstream = true;
            return;
        }

        if (!line.StartsWith(Ab, StringComparison.Ordinal))
            return;

        var rest = line[Ab.Length..];
        while (!rest.IsEmpty)
        {
            rest = rest.TrimStart(' ');
            if (rest.IsEmpty)
                break;

            var space = rest.IndexOf(' ');
            var token = space < 0 ? rest : rest[..space];
            rest = space < 0 ? ReadOnlySpan<char>.Empty : rest[(space + 1)..];

            if (token.Length > 1 && token[0] == '+' && int.TryParse(token[1..], out var a))
                ahead = a;
            else if (token.Length > 1 && token[0] == '-' && int.TryParse(token[1..], out var b))
                behind = b;
        }
    }

    private static void CountXy(ReadOnlySpan<char> line, ref int staged, ref int unstaged)
    {
        // "<type> <XY> ..." — XY are the two chars after the leading type + space.
        if (line.Length < 4)
            return;

        var x = line[2];
        var y = line[3];
        if (x is not ('.' or ' '))
            staged++;
        if (y is not ('.' or ' '))
            unstaged++;
    }

    private static GitFileStatus ClassifyOrdinary(ReadOnlySpan<char> line)
    {
        if (line.Length < 4)
            return GitFileStatus.None;

        var x = line[2];
        var y = line[3];

        // Working-tree dirt wins over staged-only.
        if (y is 'M' or 'D' or 'T' or 'R' or 'C' or 'A' or 'U')
            return GitFileStatus.Modified;

        if (x is 'A' or 'M' or 'D' or 'R' or 'C' or 'T')
            return GitFileStatus.AddedOrStaged;

        if (y is not ('.' or ' '))
            return GitFileStatus.Modified;
        if (x is not ('.' or ' '))
            return GitFileStatus.AddedOrStaged;

        return GitFileStatus.None;
    }

    private static string? ExtractUntrackedPath(ReadOnlySpan<char> line)
    {
        // "? <path>" or "? <path with spaces>"
        if (line.Length < 3 || line[1] != ' ')
            return null;
        return NormalizePath(line[2..].ToString());
    }

    private static string? ExtractOrdinaryPath(ReadOnlySpan<char> line)
    {
        // Ordinary / rename / unmerged lines end with the path (rename: path\torig).
        // Walk fields until the last remaining span for type '1' / 'u', and for '2'
        // take the substring before the tab.
        var tab = line.IndexOf('\t');
        var body = tab >= 0 ? line[..tab] : line;

        // Skip fixed fields: type, XY, sub, then N octal/hashes depending on type.
        // Safer: find the last field after a known minimum of tokens.
        var start = FindPathStart(body);
        if (start < 0 || start >= body.Length)
            return null;

        return NormalizePath(body[start..].ToString());
    }

    private static int FindPathStart(ReadOnlySpan<char> line)
    {
        // Porcelain v2 ordinary changed: "1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>"
        // Rename: "2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>"
        // Unmerged: "u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>"
        var fieldCount = line[0] switch
        {
            '1' => 8, // type + 7 fields before path
            '2' => 9,
            'u' => 10,
            _ => -1
        };
        if (fieldCount < 0)
            return -1;

        var index = 0;
        for (var field = 0; field < fieldCount; field++)
        {
            while (index < line.Length && line[index] == ' ')
                index++;
            while (index < line.Length && line[index] != ' ')
                index++;
        }

        while (index < line.Length && line[index] == ' ')
            index++;

        return index < line.Length ? index : -1;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.Trim().Replace('\\', '/').TrimEnd('/');
        return path.Length == 0 ? null : path;
    }

    private static void Upsert(Dictionary<string, GitFileStatus> files, string? path, GitFileStatus status)
    {
        if (path is null || status == GitFileStatus.None)
            return;

        if (files.TryGetValue(path, out var existing))
            files[path] = GitStatusSnapshot.Max(existing, status);
        else
            files[path] = status;
    }
}
