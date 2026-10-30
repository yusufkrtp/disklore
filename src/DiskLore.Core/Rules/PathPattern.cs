using System.Runtime.CompilerServices;
using DiskLore.Core.Model;

namespace DiskLore.Core.Rules;

// Segment-wise glob over absolute paths.
//   *   one segment, or any run of characters inside a segment (*CanonicalGroup*)
//   **  zero or more segments
// Tokens (%WINDIR%, ~, ...) are expanded at construction. Profile-relative tokens also match
// other user profiles: <users-root>\*\<rest>. Paths are compared as segment lists, so
// "C:\Users\Alice" is ["C:", "Users", "Alice"] and "/home/y" is ["home", "y"].
public sealed class PathPattern
{
    private static readonly bool IgnoreCase = !OperatingSystem.IsLinux();
    private static readonly char[] Separators = ['\\', '/'];

    private static readonly ConditionalWeakTable<ScanNode, string[]> RootSegments = new();

    [ThreadStatic] private static string[]? t_buffer;

    private readonly Seg[] _segments;

    public PathPattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Pattern = pattern;
        _segments = Compile(pattern);
        LastLiteral = _segments.Length > 0 && !_segments[^1].DoubleStar && !_segments[^1].HasWildcard
            ? _segments[^1].Text
            : null;
    }

    public string Pattern { get; }

    // The final segment when it is a plain name; used to index rules by the last path component.
    internal string? LastLiteral { get; }

    internal static bool CaseInsensitive => IgnoreCase;

    public bool IsMatch(string fullPath)
    {
        var parts = SplitSegments(fullPath);
        return Match(parts, parts.Length);
    }

    internal bool IsMatch(ScanNode node)
    {
        int n = FillSegments(node, out var buf);
        return Match(buf, n);
    }

    // True if the pattern matches some proper ancestor of the segment list (a prefix shorter than the whole).
    internal bool MatchesAncestorOf(string[] segments, int count)
    {
        for (int k = count - 1; k >= 1; k--)
            if (Match(segments, k)) return true;
        return false;
    }

    internal bool Match(string[] text, int n)
    {
        var pat = _segments;
        int m = pat.Length, p = 0, t = 0, starP = -1, starT = 0;
        while (t < n)
        {
            if (p < m && pat[p].DoubleStar)
            {
                starP = p++;
                starT = t;
            }
            else if (p < m && SegmentMatches(pat[p], text[t]))
            {
                p++;
                t++;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else return false;
        }
        while (p < m && pat[p].DoubleStar) p++;
        return p == m;
    }

    // Fills a per-thread buffer with the node's path segments (root first). The buffer is only valid
    // until the next call on the same thread.
    internal static int FillSegments(ScanNode node, out string[] buffer)
    {
        int depth = 0;
        var root = node;
        while (root.Parent != null)
        {
            depth++;
            root = root.Parent;
        }

        var rootSegs = RootSegments.GetValue(root, static r => SplitSegments(r.Name));
        int total = rootSegs.Length + depth;
        var buf = t_buffer;
        if (buf == null || buf.Length < total) t_buffer = buf = new string[Math.Max(total, 64)];

        int i = total - 1;
        for (var n = node; n.Parent != null; n = n.Parent) buf[i--] = n.Name;
        Array.Copy(rootSegs, buf, rootSegs.Length);
        buffer = buf;
        return total;
    }

    internal static string[] SplitSegments(string path) =>
        path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    internal static bool GlobMatch(string pattern, string text) => GlobMatch(pattern.AsSpan(), text.AsSpan());

    internal static bool GlobMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        int p = 0, t = 0, starP = -1, starT = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (p < pattern.Length && CharEq(pattern[p], text[t]))
            {
                p++;
                t++;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool CharEq(char a, char b) =>
        a == b || (IgnoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

    private static bool SegmentMatches(Seg seg, string text) =>
        seg.HasWildcard ? GlobMatch(seg.Text.AsSpan(), text.AsSpan())
        : IgnoreCase ? string.Equals(seg.Text, text, StringComparison.OrdinalIgnoreCase)
        : string.Equals(seg.Text, text, StringComparison.Ordinal);

    private readonly record struct Seg(string Text, bool DoubleStar, bool HasWildcard);

    private static Seg[] Compile(string pattern)
    {
        var expanded = Expand(pattern, out bool rooted);
        var raw = SplitSegments(expanded);
        var segs = new List<Seg>(raw.Length + 1);
        // A relative pattern (no token, no drive, no leading slash) may match anywhere.
        if (!rooted && !(raw.Length > 0 && raw[0] == "**")) segs.Add(new Seg("**", true, true));
        foreach (var r in raw)
        {
            if (r == "**") segs.Add(new Seg(r, true, true));
            else segs.Add(new Seg(r, false, r.Contains('*')));
        }
        return segs.ToArray();
    }

    private static string Expand(string pattern, out bool rooted)
    {
        rooted = false;
        var s = pattern.Trim();
        if (s.Length == 0) throw new ArgumentException("Empty path pattern.");

        if (s[0] == '~' && (s.Length == 1 || s[1] is '/' or '\\'))
        {
            rooted = true;
            return ProfileRelative("", s.Length > 1 ? s[2..] : "");
        }

        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != '%')
            {
                sb.Append(s[i++]);
                continue;
            }
            int end = s.IndexOf('%', i + 1);
            if (end < 0) throw new ArgumentException($"Unterminated token in '{pattern}'.");
            var token = s.Substring(i + 1, end - i - 1).ToUpperInvariant();
            i = end + 1;
            rooted = true;

            string? rel = token switch
            {
                "USERPROFILE" or "HOME" => "",
                "LOCALAPPDATA" => RelToProfile(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                "APPDATA" => RelToProfile(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                _ => null,
            };

            if (rel != null)
            {
                // Profile-relative: consume the rest and compile to <users-root>\*\<rel>\<rest>.
                var rest = s[i..].TrimStart('\\', '/');
                if (sb.Length > 0) throw new ArgumentException($"Token %{token}% must start the pattern '{pattern}'.");
                return rel.StartsWith('\u0001')
                    ? Join(rel[1..], rest) // lives outside the profile: single literal path
                    : ProfileRelative(rel, rest);
            }

            sb.Append(token switch
            {
                "WINDIR" => Required(Environment.GetFolderPath(Environment.SpecialFolder.Windows), token),
                "SYSTEMDRIVE" => Required(SystemDrive(), token),
                "PROGRAMFILES" => Required(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), token),
                "PROGRAMFILESX86" => Required(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), token),
                "PROGRAMDATA" => Required(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), token),
                "TEMP" => Required(Path.GetTempPath().TrimEnd('\\', '/'), token),
                _ => throw new ArgumentException($"Unknown token %{token}% in '{pattern}'."),
            });
        }

        var result = sb.ToString();
        var t = result.TrimStart();
        rooted = rooted
                 || t.StartsWith('/') || t.StartsWith('\\')
                 || (t.Length >= 2 && t[1] == ':');
        return result;
    }

    private static string Required(string value, string token) =>
        string.IsNullOrEmpty(value) ? throw new ArgumentException($"Token %{token}% is not available on this system.") : value;

    private static string SystemDrive()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = string.IsNullOrEmpty(win) ? "" : Path.GetPathRoot(win) ?? "";
        return root.TrimEnd('\\', '/');
    }

    private static string Profile() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Path of a special folder relative to the profile; "\u0001"-prefixed absolute path if it is elsewhere.
    private static string RelToProfile(string folder)
    {
        var profile = Profile();
        if (string.IsNullOrEmpty(folder)) throw new ArgumentException("Profile folder is not available on this system.");
        if (!string.IsNullOrEmpty(profile))
        {
            var p = profile.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var cmp = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (folder.StartsWith(p, cmp)) return folder[p.Length..];
        }
        return "\u0001" + folder;
    }

    private static string Join(string a, string b) =>
        b.Length == 0 ? a : a.Length == 0 ? b : a.TrimEnd('\\', '/') + Path.DirectorySeparatorChar + b;

    private static string ProfileRelative(string rel, string rest)
    {
        var profile = Profile().TrimEnd('\\', '/');
        if (profile.Length == 0) throw new ArgumentException("User profile is not available on this system.");
        var tail = Join(rel, rest);
        var parent = Path.GetDirectoryName(profile);
        // Without a real users folder (e.g. /root) there is nothing to wildcard over.
        var usersRoot = parent == null ? "" : parent;
        if (SplitSegments(usersRoot).Length == 0) return Join(profile, tail);
        var star = usersRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar + "*";
        return Join(star, tail);
    }
}
