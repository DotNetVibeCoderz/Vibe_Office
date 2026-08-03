using System.Text;
using System.Text.RegularExpressions;

namespace AutoWork.Core.Security;

/// <summary>
/// Minimal glob matcher for the denied-pattern list: <c>*</c> (within a segment),
/// <c>**</c> (across segments) and <c>?</c>. Paths are normalised to forward slashes
/// before matching so one pattern works on every platform.
/// </summary>
public static class Glob
{
    private static readonly Dictionary<string, Regex> _cache = new(StringComparer.Ordinal);
    private static readonly Lock _gate = new();

    public static bool IsMatch(string path, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        return GetRegex(pattern).IsMatch(Normalize(path));
    }

    public static bool IsMatchAny(string path, IEnumerable<string> patterns)
    {
        var normalized = Normalize(path);
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrEmpty(pattern) && GetRegex(pattern).IsMatch(normalized))
                return true;
        }
        return false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static Regex GetRegex(string pattern)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(pattern, out var cached))
                return cached;

            var regex = new Regex(Translate(Normalize(pattern)),
                RegexOptions.Compiled | RegexOptions.CultureInvariant |
                (OperatingSystem.IsLinux() ? RegexOptions.None : RegexOptions.IgnoreCase));

            _cache[pattern] = regex;
            return regex;
        }
    }

    private static string Translate(string pattern)
    {
        var sb = new StringBuilder("^");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        i++;
                        // "**/" should also match zero directories, so "**/x" matches "x".
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                        {
                            i++;
                            sb.Append("(?:.*/)?");
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;

                case '?':
                    sb.Append("[^/]");
                    break;

                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
