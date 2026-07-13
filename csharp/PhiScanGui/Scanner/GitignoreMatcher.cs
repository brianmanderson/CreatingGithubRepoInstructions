using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PhiScanGui.Scanner
{
    /// <summary>
    /// Evaluates the scan root's .gitignore so findings already covered by it can be
    /// reported as such. Implements the core gitignore semantics: last match wins,
    /// '!' negation, trailing-slash directory patterns, leading/middle-slash anchoring,
    /// '*', '?', '[...]' and '**'. An ignored parent directory covers everything
    /// beneath it (and negations cannot re-include inside it), matching git.
    /// Limitations (shared with tools/phi_scan.py): only the ROOT .gitignore is read —
    /// nested .gitignore files and global/core.excludesFile are not consulted.
    /// </summary>
    public class GitignoreMatcher
    {
        private class Rule
        {
            public Regex Regex;
            public bool Negation;
            public bool DirOnly;
        }

        private readonly List<Rule> _rules = new List<Rule>();

        public bool HasRules => _rules.Count > 0;

        /// <summary>Load the .gitignore at <paramref name="root"/>; empty matcher if none.</summary>
        public static GitignoreMatcher Load(string root)
        {
            var m = new GitignoreMatcher();
            var path = Path.Combine(root, ".gitignore");
            if (File.Exists(path))
                foreach (var line in File.ReadAllLines(path))
                    m.AddPattern(line);
            return m;
        }

        public void AddPattern(string line)
        {
            var pat = line.TrimEnd(); // git strips trailing spaces (unless escaped; rare)
            if (pat.Length == 0 || pat[0] == '#')
                return;

            bool negation = pat[0] == '!';
            if (negation) pat = pat.Substring(1);
            if (pat.StartsWith("\\#") || pat.StartsWith("\\!")) pat = pat.Substring(1);

            bool dirOnly = pat.EndsWith("/");
            if (dirOnly) pat = pat.TrimEnd('/');
            if (pat.Length == 0) return;

            // A slash at the start or middle anchors the pattern to the root.
            bool anchored = pat.StartsWith("/") || pat.IndexOf('/') >= 0;
            pat = pat.TrimStart('/');

            var body = GlobToRegex(pat);
            var full = (anchored ? "^" : "^(?:.*/)?") + body + "$";
            _rules.Add(new Rule
            {
                Regex = new Regex(full, RegexOptions.Compiled),
                Negation = negation,
                DirOnly = dirOnly,
            });
        }

        /// <summary>Is this root-relative path (forward slashes) ignored?</summary>
        public bool IsIgnored(string relativePath, bool isDirectory)
        {
            var path = relativePath.Trim('/');
            if (path.Length == 0) return false;

            // If any ancestor directory is ignored, the whole subtree is ignored
            // and negations inside cannot re-include (git behavior).
            var parts = path.Split('/');
            var prefix = new StringBuilder();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (i > 0) prefix.Append('/');
                prefix.Append(parts[i]);
                if (Decide(prefix.ToString(), isDirectory: true))
                    return true;
            }
            return Decide(path, isDirectory);
        }

        private bool Decide(string path, bool isDirectory)
        {
            bool ignored = false;
            foreach (var rule in _rules)
            {
                if (rule.DirOnly && !isDirectory) continue;
                if (rule.Regex.IsMatch(path))
                    ignored = !rule.Negation;
            }
            return ignored;
        }

        private static string GlobToRegex(string pat)
        {
            var sb = new StringBuilder();
            int i = 0, n = pat.Length;
            while (i < n)
            {
                char c = pat[i];
                if (c == '/' && i + 2 == n - 1 && pat[i + 1] == '*' && pat[i + 2] == '*')
                {
                    // trailing '/**': everything inside the dir AND the dir itself
                    // (git check-ignore treats 'temp/**' as matching 'temp/')
                    sb.Append("(?:/.*)?");
                    i += 3;
                    continue;
                }
                if (c == '*')
                {
                    bool isDoubleStar = i + 1 < n && pat[i + 1] == '*';
                    bool prevIsBoundary = i == 0 || pat[i - 1] == '/';
                    if (isDoubleStar && prevIsBoundary && i + 2 < n && pat[i + 2] == '/')
                    { sb.Append("(?:.*/)?"); i += 3; continue; }   // '**/' — any depth, incl. none
                    if (isDoubleStar && prevIsBoundary && i + 2 >= n)
                    { sb.Append(".*"); i += 2; continue; }          // trailing '**' — everything
                    sb.Append("[^/]*"); i += isDoubleStar ? 2 : 1;  // plain '*' (or '**' mid-segment)
                    continue;
                }
                if (c == '?') { sb.Append("[^/]"); i++; continue; }
                if (c == '[')
                {
                    int close = pat.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        var cls = pat.Substring(i + 1, close - i - 1);
                        if (cls.StartsWith("!")) cls = "^" + cls.Substring(1);
                        sb.Append('[').Append(cls).Append(']');
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
            return sb.ToString();
        }
    }
}
