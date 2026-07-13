namespace PhiScanGui.Scanner
{
    public enum Severity
    {
        High = 0,    // almost certainly PHI; move OUT of the folder
        Medium = 1,  // open and judge each one
        Review = 2,  // opaque format; a human must open it
    }

    public class Finding
    {
        public Severity Severity { get; }
        /// <summary>Path relative to the scan root, forward slashes (gitignore style).</summary>
        public string RelativePath { get; }
        public string Reason { get; }
        /// <summary>True when the finding is a directory (gitignore entry needs a trailing slash).</summary>
        public bool IsDirectory { get; }

        /// <summary>
        /// True when the root .gitignore already covers this path, so git will not
        /// pick it up. Caveat: covers UNTRACKED files only — a file git already
        /// tracks stays tracked regardless of .gitignore.
        /// </summary>
        public bool IsCoveredByGitignore { get; set; }

        public Finding(Severity severity, string relativePath, string reason, bool isDirectory = false)
        {
            Severity = severity;
            RelativePath = relativePath.Replace('\\', '/');
            Reason = reason;
            IsDirectory = isDirectory;
        }
    }
}
