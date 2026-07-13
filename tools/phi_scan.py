#!/usr/bin/env python3
"""
phi_scan.py — scan a folder for likely PHI before it goes into a git repository.

Stdlib only; Python 3.8+; Windows/macOS/Linux.

    python phi_scan.py <folder>                      report findings
    python phi_scan.py <folder> --update-gitignore   also append flagged paths to
                                                     <folder>/.gitignore
    python phi_scan.py <folder> --json               machine-readable output

Exit codes: 0 = clean, 1 = findings, 2 = usage/IO error. That makes it usable as a
pre-commit hook or CI step (see templates/). Findings already covered by the root
.gitignore are reported informationally but do not fail the scan — git cannot pick
them up (unless it already tracks them, which .gitignore does not undo).

What it detects
  HIGH   - DICOM files, by extension AND by magic bytes ('DICM' at offset 128), so
           extensionless exports are caught too
         - CSV/text files whose header row has PHI-shaped columns (MRN, DOB, SSN,
           patient name)
  MEDIUM - text content: SSN patterns, 'MRN: 1234567', DOB lines, DICOM tag dumps
           (PatientName/PatientID), passwords in connection strings
         - medical volume formats (.nii/.mha/...) which are frequently named by MRN
         - filenames that are bare 6-9 digit numbers (MRN-shaped)
  REVIEW - opaque formats the scanner cannot read (Excel, Access, PDF, Word,
           databases, backups) — a human must open these

A finding is a *flag for human judgment*, not a verdict. A clean run is NOT proof
of no PHI — this tool narrows the search; it does not replace looking.
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

MAX_CONTENT_BYTES = 5 * 1024 * 1024  # per-file cap for content scanning

SKIP_DIRS = {
    ".git", ".svn", ".hg", "node_modules", "__pycache__", ".venv", "venv", "env",
    ".vs", ".idea", "bin", "obj", "packages", ".pytest_cache", ".ipynb_checkpoints",
}

# Tier by extension ------------------------------------------------------------
DICOM_EXTS = {".dcm", ".dicom", ".ima"}
VOLUME_EXTS = {".nii", ".mha", ".mhd", ".nrrd", ".raw", ".img", ".hdr", ".dvh"}
REVIEW_EXTS = {
    ".xls", ".xlsx", ".xlsm", ".accdb", ".mdb", ".db", ".sqlite", ".sqlite3",
    ".pdf", ".doc", ".docx", ".rtf", ".bak", ".pst", ".msg",
}
TEXT_EXTS = {
    ".txt", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".md", ".log",
    ".py", ".cs", ".sql", ".ini", ".cfg", ".config", ".ps1", ".bat", ".sh",
    ".xaml", ".csproj", ".sln", ".html", ".js", ".r", ".m",
}

# Content patterns (compiled once) ----------------------------------------------
CONTENT_PATTERNS = [
    ("SSN pattern",
     re.compile(r"\b\d{3}-\d{2}-\d{4}\b")),
    ("MRN with value",
     re.compile(r"(?i)\bMRN\b\s*[:#=\"']?\s*\d{5,10}\b")),
    ("Date of birth field",
     re.compile(r"(?i)\b(DOB|date\s*of\s*birth|birth\s*date)\b\s*[:=\"']?\s*[\d/\-]{6,10}")),
    ("DICOM patient tag dump",
     re.compile(r"(?i)\bpatient['\"]?s?\s*(name|id|birth)")),
    ("Password in connection string / config",
     re.compile(r"(?i)\b(password|pwd)\s*=\s*[^;\s\"']{3,}")),
    ("Hardcoded patient ID in code",
     re.compile(r"(?i)\b(patientid|patient_id|pat_id)\s*[:=]\s*[\"']?\d{5,10}")),
]

# CSV/TSV header columns that indicate a patient-level export
PHI_COLUMNS = re.compile(
    r"(?i)^\s*(mrn|medical\s*record|patient\s*_?(id|name)|pat_?id|ssn|social\s*security"
    r"|dob|date\s*of\s*birth|birth\s*date|last\s*_?name|first\s*_?name)\s*$"
)

MRN_SHAPED_NAME = re.compile(r"^\d{6,9}$")  # bare 6-9 digit file/folder name


class Finding:
    __slots__ = ("severity", "path", "reason", "is_dir", "covered")

    def __init__(self, severity, path, reason, is_dir=False):
        self.severity = severity   # HIGH | MEDIUM | REVIEW
        self.path = path           # Path, relative to scan root
        self.reason = reason
        self.is_dir = is_dir
        self.covered = False       # already matched by the root .gitignore


class GitignoreMatcher:
    """Evaluates the scan root's .gitignore (root file only — nested .gitignore
    files and global excludes are not consulted). Implements core semantics:
    last match wins, '!' negation, trailing-slash dir-only patterns,
    leading/middle-slash anchoring, '*', '?', '[...]', '**', and 'an ignored
    parent directory covers everything beneath it'. Kept in lockstep with the
    C# port in csharp/PhiScanGui/Scanner/GitignoreMatcher.cs."""

    def __init__(self):
        self.rules = []  # (regex, negation, dir_only)

    @classmethod
    def load(cls, root):
        m = cls()
        gi = Path(root) / ".gitignore"
        if gi.exists():
            # utf-8-sig: Notepad/PowerShell write a BOM, which would otherwise
            # glue itself onto the first pattern and silently disable it
            for line in gi.read_text(encoding="utf-8-sig", errors="replace").splitlines():
                m.add_pattern(line)
        return m

    def add_pattern(self, line):
        pat = line.rstrip()
        if not pat or pat.startswith("#"):
            return
        negation = pat.startswith("!")
        if negation:
            pat = pat[1:]
        if pat.startswith(("\\#", "\\!")):
            pat = pat[1:]
        dir_only = pat.endswith("/")
        pat = pat.rstrip("/")
        if not pat:
            return
        anchored = pat.startswith("/") or "/" in pat
        pat = pat.lstrip("/")
        body = self._glob_to_regex(pat)
        prefix = "^" if anchored else "^(?:.*/)?"
        self.rules.append((re.compile(prefix + body + "$"), negation, dir_only))

    def is_ignored(self, rel_path, is_dir):
        path = str(rel_path).replace("\\", "/").strip("/")
        if not path:
            return False
        parts = path.split("/")
        # An ignored ancestor directory covers the whole subtree (negations
        # inside cannot re-include) — matches git behavior.
        for i in range(1, len(parts)):
            if self._decide("/".join(parts[:i]), is_dir=True):
                return True
        return self._decide(path, is_dir)

    def _decide(self, path, is_dir):
        ignored = False
        for regex, negation, dir_only in self.rules:
            if dir_only and not is_dir:
                continue
            if regex.match(path):
                ignored = not negation
        return ignored

    @staticmethod
    def _glob_to_regex(pat):
        out = []
        i, n = 0, len(pat)
        while i < n:
            c = pat[i]
            if c == "/" and i + 2 == n - 1 and pat[i + 1:i + 3] == "**":
                # trailing '/**': everything inside the dir AND the dir itself
                # (git check-ignore treats 'temp/**' as matching 'temp/')
                out.append("(?:/.*)?")
                i += 3
                continue
            if c == "*":
                double = i + 1 < n and pat[i + 1] == "*"
                boundary = i == 0 or pat[i - 1] == "/"
                if double and boundary and i + 2 < n and pat[i + 2] == "/":
                    out.append("(?:.*/)?"); i += 3; continue   # '**/'
                if double and boundary and i + 2 >= n:
                    out.append(".*"); i += 2; continue          # trailing '**'
                out.append("[^/]*"); i += 2 if double else 1; continue
            if c == "?":
                out.append("[^/]"); i += 1; continue
            if c == "[":
                close = pat.find("]", i + 1)
                if close > i:
                    cls = pat[i + 1:close]
                    if cls.startswith("!"):
                        cls = "^" + cls[1:]
                    out.append("[" + cls + "]"); i = close + 1; continue
            out.append(re.escape(c)); i += 1
        return "".join(out)


def is_dicom_by_magic(fp):
    """True if the file has the DICOM preamble ('DICM' at byte offset 128)."""
    try:
        with open(fp, "rb") as f:
            head = f.read(132)
        return len(head) == 132 and head[128:132] == b"DICM"
    except OSError:
        return False


def sniff_text(fp):
    """Return decoded text (best effort) or None if the file looks binary."""
    try:
        with open(fp, "rb") as f:
            raw = f.read(MAX_CONTENT_BYTES)
    except OSError:
        return None
    if b"\x00" in raw[:8192]:
        return None
    for enc in ("utf-8", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return None


def scan_csv_header(text):
    """Return list of PHI-shaped column names in the first line, if delimited."""
    first = text.split("\n", 1)[0].strip().lstrip("﻿")
    if not first:
        return []
    delim = "\t" if "\t" in first else ","
    cols = [c.strip().strip('"') for c in first.split(delim)]
    return [c for c in cols if PHI_COLUMNS.match(c)]


def scan_text_content(text):
    """Return list of (reason, example) for pattern hits in text."""
    hits = []
    for reason, pat in CONTENT_PATTERNS:
        m = pat.search(text)
        if m:
            hits.append((reason, m.group(0)[:60]))
    return hits


def scan_tree(root):
    findings = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        rel_dir = Path(dirpath).relative_to(root)

        for d in dirnames:
            if MRN_SHAPED_NAME.match(d):
                findings.append(Finding(
                    "MEDIUM", rel_dir / d,
                    "folder name is a bare 6-9 digit number (MRN-shaped)",
                    is_dir=True))

        for name in filenames:
            fp = Path(dirpath) / name
            rel = rel_dir / name
            ext = "".join(fp.suffixes[-2:]).lower() if fp.suffixes else ""
            last_ext = fp.suffix.lower()

            if last_ext in DICOM_EXTS:
                findings.append(Finding("HIGH", rel, "DICOM file extension"))
                continue
            if last_ext in VOLUME_EXTS or ext.endswith(".nii.gz"):
                sev = "MEDIUM"
                reason = "medical volume format (may carry PHI in header or filename)"
                if MRN_SHAPED_NAME.match(fp.stem.split(".")[0]):
                    sev, reason = "HIGH", "medical volume named by MRN-shaped number"
                findings.append(Finding(sev, rel, reason))
                continue
            if last_ext in REVIEW_EXTS:
                findings.append(Finding(
                    "REVIEW", rel,
                    f"opaque format ({last_ext}) — open it and check for PHI manually"))
                continue

            # Extensionless or unknown binary: check DICOM magic bytes
            if last_ext not in TEXT_EXTS and is_dicom_by_magic(fp):
                findings.append(Finding(
                    "HIGH", rel, "DICOM magic bytes (DICM header) despite extension"))
                continue

            if MRN_SHAPED_NAME.match(fp.stem):
                findings.append(Finding(
                    "MEDIUM", rel, "filename is a bare 6-9 digit number (MRN-shaped)"))

            if last_ext in TEXT_EXTS:
                text = sniff_text(fp)
                if text is None:
                    continue
                if last_ext in (".csv", ".tsv"):
                    cols = scan_csv_header(text)
                    if cols:
                        findings.append(Finding(
                            "HIGH", rel,
                            "PHI-shaped columns in header: " + ", ".join(cols)))
                        continue
                for reason, example in scan_text_content(text):
                    findings.append(Finding(
                        "MEDIUM", rel, f"{reason} (e.g. \"{example}\")"))
    return findings


# .gitignore update --------------------------------------------------------------

MANAGED_HEADER = "# --- added by phi_scan.py (review, then keep or prune) ---"


def gitignore_entries(findings):
    """Repo-relative gitignore patterns for uncovered HIGH findings (dirs collapse files)."""
    entries = []
    seen_dirs = set()
    for f in findings:
        if f.severity != "HIGH" or f.covered:
            continue
        parent = f.path.parent
        if parent != Path("."):
            top = parent.parts[0]
            if top not in seen_dirs:
                seen_dirs.add(top)
                entries.append(top + "/")
        else:
            entries.append(f.path.as_posix())
    return entries


def update_gitignore(root, entries):
    gi = root / ".gitignore"
    existing = gi.read_text(encoding="utf-8").splitlines() if gi.exists() else []
    existing_set = {line.strip() for line in existing}
    new = [e for e in entries if e not in existing_set]
    if not new:
        return []
    block = []
    if MANAGED_HEADER not in existing_set:
        block.append(MANAGED_HEADER)
    block.extend(new)
    with open(gi, "a", encoding="utf-8", newline="\n") as f:
        if existing and existing[-1].strip():
            f.write("\n")
        f.write("\n".join(block) + "\n")
    return new


# Report --------------------------------------------------------------------------

def print_report(findings, root):
    order = {"HIGH": 0, "MEDIUM": 1, "REVIEW": 2}
    findings.sort(key=lambda f: (order[f.severity], str(f.path)))
    counts = {"HIGH": 0, "MEDIUM": 0, "REVIEW": 0}
    for f in findings:
        counts[f.severity] += 1

    if not findings:
        print(f"phi_scan: no PHI indicators found under {root}")
        print("Reminder: a clean scan narrows the search, it does not prove absence.")
        return

    print(f"phi_scan: {len(findings)} finding(s) under {root}\n")
    for sev in ("HIGH", "MEDIUM", "REVIEW"):
        group = [f for f in findings if f.severity == sev]
        if not group:
            continue
        label = {
            "HIGH": "HIGH — almost certainly PHI; move OUT of the folder",
            "MEDIUM": "MEDIUM — open and judge each one",
            "REVIEW": "REVIEW — scanner cannot read these; check manually",
        }[sev]
        print(f"[{label}]")
        for f in group:
            mark = "  [already in .gitignore]" if f.covered else ""
            print(f"  {f.path}  <- {f.reason}{mark}")
        print()
    covered = sum(1 for f in findings if f.covered)
    print(f"Totals: HIGH={counts['HIGH']}  MEDIUM={counts['MEDIUM']}  REVIEW={counts['REVIEW']}"
          + (f"  (already covered by .gitignore: {covered})" if covered else ""))
    if covered:
        print("Covered files are safe from 'git add' — but NOT if git already tracks them,")
        print("and .gitignore does not make the folder a safe place to keep PHI.")
    print("Best fix is moving data out of the repo folder; --update-gitignore is the backstop.")


def main(argv=None):
    ap = argparse.ArgumentParser(description="Scan a folder for likely PHI before committing it to git.")
    ap.add_argument("root", nargs="?", default=".", help="folder to scan (default: current)")
    ap.add_argument("--update-gitignore", action="store_true",
                    help="append HIGH findings to <root>/.gitignore")
    ap.add_argument("--json", action="store_true", help="emit JSON instead of text")
    args = ap.parse_args(argv)

    root = Path(args.root).resolve()
    if not root.is_dir():
        print(f"phi_scan: not a directory: {root}", file=sys.stderr)
        return 2

    findings = scan_tree(root)

    matcher = GitignoreMatcher.load(root)
    if matcher.rules:
        for f in findings:
            f.covered = matcher.is_ignored(f.path, f.is_dir)

    added = []
    if args.update_gitignore:
        added = update_gitignore(root, gitignore_entries(findings))

    if args.json:
        print(json.dumps({
            "root": str(root),
            "findings": [
                {"severity": f.severity, "path": f.path.as_posix(), "reason": f.reason,
                 "covered_by_gitignore": f.covered}
                for f in findings
            ],
            "gitignore_added": added,
        }, indent=2))
    else:
        print_report(findings, root)
        if args.update_gitignore:
            if added:
                print(f"\n.gitignore: added {len(added)} entr{'y' if len(added)==1 else 'ies'}: "
                      + ", ".join(added))
                print("NOTE: .gitignore does not untrack already-committed files "
                      "(git rm --cached <file>) and does not protect files already pushed.")
            else:
                print("\n.gitignore: nothing to add (no HIGH findings, or already covered).")

    # Findings already covered by .gitignore don't fail the scan: git can't pick
    # them up, so the pre-commit hook and CI shouldn't block on them.
    return 1 if any(not f.covered for f in findings) else 0


if __name__ == "__main__":
    sys.exit(main())
