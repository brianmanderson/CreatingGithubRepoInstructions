using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PhiScanGui.Scanner
{
    /// <summary>Appends selected findings to the scan root's .gitignore, idempotently.</summary>
    public static class GitignoreUpdater
    {
        // Same managed-block header as tools/phi_scan.py so the two tools share one block.
        public const string ManagedHeader = "# --- added by phi_scan.py (review, then keep or prune) ---";

        public static string EntryFor(Finding f)
            => f.IsDirectory ? f.RelativePath.TrimEnd('/') + "/" : f.RelativePath;

        /// <summary>
        /// Gitignore entries for a set of findings, mirroring phi_scan.py: HIGH files
        /// inside a subfolder collapse to that top-level folder (so files added to it
        /// later are covered too); everything else is listed explicitly.
        /// </summary>
        public static List<string> EntriesFor(IEnumerable<Finding> selected)
        {
            var entries = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in selected)
            {
                string entry;
                var slash = f.RelativePath.IndexOf('/');
                if (f.Severity == Severity.High && !f.IsDirectory && slash > 0)
                    entry = f.RelativePath.Substring(0, slash) + "/";
                else
                    entry = EntryFor(f);
                if (seen.Add(entry))
                    entries.Add(entry);
            }
            return entries;
        }

        /// <summary>Returns the entries actually added (empty if everything was already covered).</summary>
        public static List<string> Append(string root, IEnumerable<Finding> selected)
        {
            var gitignore = Path.Combine(root, ".gitignore");
            var existing = File.Exists(gitignore)
                ? File.ReadAllLines(gitignore).ToList()
                : new List<string>();
            var existingSet = new HashSet<string>(existing.Select(l => l.Trim()), StringComparer.Ordinal);

            var toAdd = EntriesFor(selected)
                                .Where(e => !existingSet.Contains(e))
                                .ToList();
            if (toAdd.Count == 0)
                return toAdd;

            var sb = new StringBuilder();
            if (existing.Count > 0 && existing[existing.Count - 1].Trim().Length > 0)
                sb.Append('\n');
            if (!existingSet.Contains(ManagedHeader))
                sb.Append(ManagedHeader).Append('\n');
            foreach (var e in toAdd)
                sb.Append(e).Append('\n');

            File.AppendAllText(gitignore, sb.ToString(), new UTF8Encoding(false));
            return toAdd;
        }
    }
}
