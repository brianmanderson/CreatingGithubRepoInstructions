using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PhiScanGui.Scanner
{
    /// <summary>
    /// Detection engine — a faithful port of tools/phi_scan.py. If you change a rule
    /// here, change it there too (and vice versa); the two must stay in lockstep so
    /// the GUI, the pre-commit hook, and the GitHub Action all agree.
    /// </summary>
    public class PhiScanner
    {
        private const int MaxContentBytes = 5 * 1024 * 1024;

        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", "node_modules", "__pycache__", ".venv", "venv", "env",
            ".vs", ".idea", "bin", "obj", "packages", ".pytest_cache", ".ipynb_checkpoints",
        };

        private static readonly HashSet<string> DicomExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".dcm", ".dicom", ".ima" };

        private static readonly HashSet<string> VolumeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".nii", ".mha", ".mhd", ".nrrd", ".raw", ".img", ".hdr", ".dvh" };

        private static readonly HashSet<string> ReviewExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xls", ".xlsx", ".xlsm", ".accdb", ".mdb", ".db", ".sqlite", ".sqlite3",
            ".pdf", ".doc", ".docx", ".rtf", ".bak", ".pst", ".msg",
        };

        private static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".md", ".log",
            ".py", ".cs", ".sql", ".ini", ".cfg", ".config", ".ps1", ".bat", ".sh",
            ".xaml", ".csproj", ".sln", ".html", ".js", ".r", ".m",
        };

        private static readonly (string Reason, Regex Pattern)[] ContentPatterns =
        {
            ("SSN pattern",
             new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)),
            ("MRN with value",
             new Regex(@"\bMRN\b\s*[:#=""']?\s*\d{5,10}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Date of birth field",
             new Regex(@"\b(DOB|date\s*of\s*birth|birth\s*date)\b\s*[:=""']?\s*[\d/\-]{6,10}", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("DICOM patient tag dump",
             new Regex(@"\bpatient['""]?s?\s*(name|id|birth)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Password in connection string / config",
             new Regex(@"\b(password|pwd)\s*=\s*[^;\s""']{3,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Hardcoded patient ID in code",
             new Regex(@"\b(patientid|patient_id|pat_id)\s*[:=]\s*[""']?\d{5,10}", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        };

        private static readonly Regex PhiColumns = new Regex(
            @"^\s*(mrn|medical\s*record|patient\s*_?(id|name)|pat_?id|ssn|social\s*security" +
            @"|dob|date\s*of\s*birth|birth\s*date|last\s*_?name|first\s*_?name)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MrnShapedName = new Regex(@"^\d{6,9}$", RegexOptions.Compiled);

        /// <summary>Scan a directory tree. Reports each file inspected via <paramref name="progress"/>.</summary>
        public List<Finding> Scan(string root, IProgress<string> progress = null, CancellationToken token = default)
        {
            var findings = new List<Finding>();
            var rootFull = Path.GetFullPath(root);
            WalkDirectory(rootFull, rootFull, findings, progress, token);
            return findings
                .OrderBy(f => f.Severity)
                .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void WalkDirectory(string dir, string root, List<Finding> findings,
                                   IProgress<string> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            IEnumerable<string> subdirs, files;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (SkipDirs.Contains(name))
                    continue;
                if (MrnShapedName.IsMatch(name))
                    findings.Add(new Finding(Severity.Medium, Relative(sub, root),
                        "folder name is a bare 6-9 digit number (MRN-shaped)", isDirectory: true));
                WalkDirectory(sub, root, findings, progress, token);
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(Relative(file, root));
                ScanFile(file, root, findings);
            }
        }

        private void ScanFile(string file, string root, List<Finding> findings)
        {
            var rel = Relative(file, root);
            var ext = Path.GetExtension(file);
            var stem = Path.GetFileNameWithoutExtension(file);
            var isNiiGz = file.EndsWith(".nii.gz", StringComparison.OrdinalIgnoreCase);

            if (DicomExts.Contains(ext))
            {
                findings.Add(new Finding(Severity.High, rel, "DICOM file extension"));
                return;
            }

            if (VolumeExts.Contains(ext) || isNiiGz)
            {
                // "1234567.nii.gz" -> stem "1234567.nii" -> take part before the first dot
                var baseName = stem.Split('.')[0];
                if (MrnShapedName.IsMatch(baseName))
                    findings.Add(new Finding(Severity.High, rel, "medical volume named by MRN-shaped number"));
                else
                    findings.Add(new Finding(Severity.Medium, rel, "medical volume format (may carry PHI in header or filename)"));
                return;
            }

            if (ReviewExts.Contains(ext))
            {
                findings.Add(new Finding(Severity.Review, rel,
                    $"opaque format ({ext.ToLowerInvariant()}) — open it and check for PHI manually"));
                return;
            }

            if (!TextExts.Contains(ext) && IsDicomByMagic(file))
            {
                findings.Add(new Finding(Severity.High, rel, "DICOM magic bytes (DICM header) despite extension"));
                return;
            }

            if (MrnShapedName.IsMatch(stem))
                findings.Add(new Finding(Severity.Medium, rel, "filename is a bare 6-9 digit number (MRN-shaped)"));

            if (!TextExts.Contains(ext))
                return;

            var text = SniffText(file);
            if (text == null)
                return;

            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase))
            {
                var cols = ScanCsvHeader(text);
                if (cols.Count > 0)
                {
                    findings.Add(new Finding(Severity.High, rel,
                        "PHI-shaped columns in header: " + string.Join(", ", cols)));
                    return;
                }
            }

            foreach (var (reason, pattern) in ContentPatterns)
            {
                var m = pattern.Match(text);
                if (m.Success)
                {
                    var example = m.Value.Length > 60 ? m.Value.Substring(0, 60) : m.Value;
                    findings.Add(new Finding(Severity.Medium, rel, $"{reason} (e.g. \"{example}\")"));
                }
            }
        }

        private static bool IsDicomByMagic(string file)
        {
            try
            {
                using (var fs = File.OpenRead(file))
                {
                    if (fs.Length < 132) return false;
                    var head = new byte[132];
                    int read = 0;
                    while (read < 132)
                    {
                        int n = fs.Read(head, read, 132 - read);
                        if (n == 0) return false;
                        read += n;
                    }
                    return head[128] == (byte)'D' && head[129] == (byte)'I'
                        && head[130] == (byte)'C' && head[131] == (byte)'M';
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static string SniffText(string file)
        {
            byte[] raw;
            try
            {
                using (var fs = File.OpenRead(file))
                {
                    var len = (int)Math.Min(fs.Length, MaxContentBytes);
                    raw = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        int n = fs.Read(raw, read, len - read);
                        if (n == 0) break;
                        read += n;
                    }
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            var probe = Math.Min(raw.Length, 8192);
            for (int i = 0; i < probe; i++)
                if (raw[i] == 0) return null;

            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding("ISO-8859-1").GetString(raw);
            }
        }

        private static List<string> ScanCsvHeader(string text)
        {
            var firstLine = text.Split('\n')[0].Trim().TrimStart('﻿');
            var result = new List<string>();
            if (firstLine.Length == 0) return result;
            var delim = firstLine.IndexOf('\t') >= 0 ? '\t' : ',';
            foreach (var col in firstLine.Split(delim))
            {
                var c = col.Trim().Trim('"');
                if (PhiColumns.IsMatch(c))
                    result.Add(c);
            }
            return result;
        }

        private static string Relative(string path, string root)
        {
            var full = Path.GetFullPath(path);
            var rel = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length).TrimStart('\\', '/')
                : full;
            return rel.Replace('\\', '/');
        }
    }
}
