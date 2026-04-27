using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitUI.Services;

/// <summary>
/// Minimal .gitignore matcher. Supports basic glob (*, **, ?), directory-only (trailing /),
/// negation (!), absolute (leading /). Not 100% spec-compliant — covers the common cases.
/// </summary>
public class GitignoreMatcher
{
    private readonly List<Rule> _rules = new();
    private readonly string _root;

    private record Rule(Regex Regex, bool Negate, bool DirOnly);

    public GitignoreMatcher(string root, IEnumerable<string> patterns)
    {
        _root = root.Replace('\\', '/').TrimEnd('/');
        foreach (var raw in patterns)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            bool negate = line.StartsWith("!");
            if (negate) line = line[1..];

            bool dirOnly = line.EndsWith("/");
            if (dirOnly) line = line[..^1];

            // Anchored if it starts with / or contains / in the middle
            bool anchored = line.StartsWith("/") || line.Contains('/');
            if (line.StartsWith("/")) line = line[1..];

            var regex = GlobToRegex(line, anchored);
            _rules.Add(new Rule(regex, negate, dirOnly));
        }
    }

    public static GitignoreMatcher LoadFrom(string folderPath)
    {
        var giPath = Path.Combine(folderPath, ".gitignore");
        if (!File.Exists(giPath)) return new GitignoreMatcher(folderPath, Array.Empty<string>());
        return new GitignoreMatcher(folderPath, File.ReadAllLines(giPath));
    }

    /// <summary>True if the given absolute path should be ignored.</summary>
    public bool IsIgnored(string absolutePath, bool isDirectory)
    {
        var rel = Path.GetRelativePath(_root, absolutePath).Replace('\\', '/');
        if (rel.StartsWith("..")) return false; // outside root

        // Always ignore .git
        if (rel == ".git" || rel.StartsWith(".git/")) return true;

        bool ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.DirOnly && !isDirectory) continue;
            if (rule.Regex.IsMatch(rel))
                ignored = !rule.Negate;
        }
        return ignored;
    }

    private static Regex GlobToRegex(string pattern, bool anchored)
    {
        // Convert simple glob to regex
        var sb = new System.Text.StringBuilder();
        sb.Append(anchored ? "^" : "(^|/)");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++;
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?') sb.Append("[^/]");
            else if ("\\.+()|{}[]^$".Contains(c)) sb.Append('\\').Append(c);
            else sb.Append(c);
        }
        sb.Append("(/.*)?$");
        return new Regex(sb.ToString(), RegexOptions.Compiled);
    }
}
