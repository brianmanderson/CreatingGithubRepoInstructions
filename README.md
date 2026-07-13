# Creating GitHub Repositories — Mindfully

A guide for the Radiation Medicine & Applied Sciences team: how to take work that
lives on your laptop or a network share and put it on GitHub **without ever letting
patient data (PHI) leave the building**.

## Why this exists

Every project's code belongs in a repository — that's a team rule, because our
project catalog links to repos and a catalog that points at nothing is a liability.
But our work sits next to DICOM files, ARIA exports, and spreadsheets full of MRNs.
GitHub is not a HIPAA-covered environment. **PHI in a repo — even a private repo —
is a reportable incident.** The single most important idea in this guide:

> **Scan and ignore first. Commit second. Push last.**

Once a file is committed, it lives in git history forever unless the history is
rewritten. Once it's pushed, it has left UCSD. Everything here is designed to catch
problems *before* the first commit, when fixing them costs nothing.

## Contents

| File | What it's for |
|---|---|
| [docs/creating-a-repo.md](docs/creating-a-repo.md) | Step-by-step: existing folder → GitHub repo (GitHub Desktop and command-line paths) |
| [docs/mindful-gitignore.md](docs/mindful-gitignore.md) | How to think about a .gitignore, not just copy one |
| [docs/pr-workflow.md](docs/pr-workflow.md) | Giving the scan teeth: branch → PR → merge, with `main` protected by the phi-scan check |
| [docs/phi-scanner-options.md](docs/phi-scanner-options.md) | Analysis: GitHub Action vs. C# tool vs. Claude prompt — and what we built |
| [tools/phi_scan.py](tools/phi_scan.py) | The PHI scanner. Run it on any folder **before** `git init`. Can auto-update your .gitignore. |
| [csharp/](csharp/README.md) | **PHI Scan desktop app** — the same scanner as a point-and-click WPF tool (no Python or terminal needed) |
| [templates/medical-project.gitignore](templates/medical-project.gitignore) | Ready-to-copy .gitignore for our typical projects |
| [templates/pre-commit](templates/pre-commit) | Git hook that runs the scanner on every commit |
| [templates/phi-scan-workflow.yml](templates/phi-scan-workflow.yml) | GitHub Action tripwire (copy into `.github/workflows/`) |
| [prompts/phi-review-prompt.md](prompts/phi-review-prompt.md) | A prompt for Claude to review borderline files the scanner flags |

## The 60-second version

```bash
# 0. You need: git, Python 3.8+, and a GitHub account. Nothing else.

# 1. BEFORE creating any repo, scan the folder:
python phi_scan.py "C:\path\to\my\project"

# 2. Fix what it finds (move data out, or let it write your .gitignore):
python phi_scan.py "C:\path\to\my\project" --update-gitignore

# 3. Only then: create the repo (PRIVATE), review `git status`, commit, push.
```

Full walkthrough in [docs/creating-a-repo.md](docs/creating-a-repo.md).

## Not a Python or terminal person? Use the desktop app

You can follow this entire process without opening a terminal:

1. **Scan with the desktop app instead of the Python script.** Download
   `PhiScanGui.exe` from the
   [Releases page](https://github.com/brianmanderson/CreatingGithubRepoInstructions/releases)
   — no installation, just double-click it. (Windows SmartScreen may warn the
   first time because the exe is unsigned; choose "More info → Run anyway".)
2. Click **Browse…**, pick your project folder, click **Scan**.
3. Work the findings top-down: **red (HIGH)** rows are almost certainly patient
   data — use **Open in Explorer** and move those files out of the folder
   entirely. **Yellow (MEDIUM)** rows need your judgment; **blue (REVIEW)** rows
   are files the scanner can't read (Excel, PDFs) — open them yourself.
4. For anything that must stay in the folder but should never be committed,
   leave its box checked and click **Add checked items to .gitignore**. The app
   shows you exactly what it will write before it writes it.
5. Then publish the folder with **GitHub Desktop** (no command line) — the
   step-by-step "Path A" instructions in
   [docs/creating-a-repo.md](docs/creating-a-repo.md) pick up from here:
   add the folder, review the file list, commit, and publish **private**.

The app runs the exact same detection rules as the Python script, so either
route gives the same protection. More detail in [csharp/README.md](csharp/README.md).

## The non-negotiables

1. **Private by default.** Public requires a deliberate decision and a second set of eyes.
2. **No PHI, ever, in any repo** — private does not mean HIPAA-compliant.
3. **Scan before the first commit.** A .gitignore added after the fact does not
   un-commit anything.
4. **Data lives outside the code folder** (or in one `data/` folder that is ignored
   wholesale). Code and patient data should never be interleaved.
5. **If PHI gets committed: stop.** Don't push. If it was already pushed, treat it
   as an incident (see the last section of the repo guide) — deleting the file in a
   new commit does *not* remove it from history.
