namespace HelixExplorer.Core.Git;

/// <summary>Parses <c>git status --porcelain=v2 -z --branch</c> output into an aggregate + file map.</summary>
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

        var span = output.AsSpan();
        while (!span.IsEmpty)
        {
            var nul = span.IndexOf('\0');
            ReadOnlySpan<char> line;
            if (nul < 0)
            {
                line = span;
                span = ReadOnlySpan<char>.Empty;
            }
            else
            {
                line = span[..nul];
                span = span[(nul + 1)..];
            }

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
        if (line.Length < 3 || line[1] != ' ')
            return null;
        return DequotePath(line[2..]);
    }

    private static string? ExtractOrdinaryPath(ReadOnlySpan<char> line)
    {
        var tab = line.IndexOf('\t');
        var body = tab >= 0 ? line[..tab] : line;

        var start = FindPathStart(body);
        if (start < 0 || start >= body.Length)
            return null;

        return DequotePath(body[start..]);
    }

    private static string? DequotePath(ReadOnlySpan<char> raw)
    {
        if (raw.Length == 0)
            return string.Empty;

        // Do NOT TrimStart here: the caller has already positioned the span at the start of the path
        // field, and a filename may legitimately begin with spaces. Only quote characters (git quotes
        // paths containing special characters) should be stripped.
        var s = raw;
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            var inner = s[1..^1];
            return UnescapeGitPath(inner);
        }

        return s.ToString().Replace('\\', '/').TrimEnd('/');
    }

    private static string? UnescapeGitPath(ReadOnlySpan<char> s)
    {
        var bytes = new List<byte>(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var next = s[i + 1];
                if (next >= '0' && next <= '7')
                {
                    var octal = next - '0';
                    i++;
                    if (i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7')
                    {
                        octal = octal * 8 + (s[++i] - '0');
                        if (i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7')
                            octal = octal * 8 + (s[++i] - '0');
                    }
                    bytes.Add((byte)octal);
                }
                else
                {
                    switch (next)
                    {
                        case 'n': bytes.Add((byte)'\n'); i++; break;
                        case 't': bytes.Add((byte)'\t'); i++; break;
                        case '\\': bytes.Add((byte)'\\'); i++; break;
                        case '"': bytes.Add((byte)'"'); i++; break;
                        case 'a': bytes.Add((byte)'\a'); i++; break;
                        case 'b': bytes.Add((byte)'\b'); i++; break;
                        case 'f': bytes.Add((byte)'\f'); i++; break;
                        case 'r': bytes.Add((byte)'\r'); i++; break;
                        case 'v': bytes.Add((byte)'\v'); i++; break;
                        default:
                            bytes.Add((byte)'\\');
                            bytes.Add((byte)next);
                            i++;
                            break;
                    }
                }
            }
            else
            {
                bytes.Add((byte)s[i]);
            }
        }

        return System.Text.Encoding.UTF8.GetString(bytes.ToArray()).Replace('\\', '/').TrimEnd('/');
    }

    private static int FindPathStart(ReadOnlySpan<char> line)
    {
        var fieldCount = line[0] switch
        {
            '1' => 8,
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
