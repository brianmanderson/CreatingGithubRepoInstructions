# From "folder on my machine" to "repository on GitHub"

This walks you through publishing existing work to GitHub, in the order that keeps
patient data safe. There are two paths — **GitHub Desktop** (no command line) and
**command line** (git + gh). Both follow the same checklist.

## The order of operations (this is the whole trick)

```
1. Scan the folder for PHI          ← before git ever sees it
2. Move data out / write .gitignore ← decide what git may NEVER see
3. git init                         ← now let git see the folder
4. Review what git plans to track   ← git status, read every line
5. First commit
6. Create the repo on GitHub (PRIVATE) and push
```

Most PHI leaks happen because people do step 3 first, commit everything, and only
then think about ignoring files. By then the data is already in history.

---

## One-time setup (per person)

1. **GitHub account** — use your existing one or create one. Ask Brian to be added
   to any shared team resources.
2. **Install one of:**
   - [GitHub Desktop](https://desktop.github.com/) — friendliest, no command line.
   - [Git for Windows](https://git-scm.com/download/win) + [GitHub CLI (`gh`)](https://cli.github.com/) —
     if you're comfortable in a terminal. Run `gh auth login` once after installing.
3. **Python 3.8+** (most of us have it) — needed only to run the PHI scanner.
4. Tell git who you are (command line only; Desktop asks during setup):
   ```bash
   git config --global user.name  "Your Name"
   git config --global user.email "you@health.ucsd.edu"
   ```

---

## Step 1 — Scan the folder BEFORE anything else

From this repo, run the scanner against your project folder:

```bash
python tools/phi_scan.py "C:\Users\you\Projects\MyEsapiScript"
```

It reports three tiers:

- **HIGH** — files that are almost certainly PHI carriers (DICOM files, files whose
  headers say `DICM`, spreadsheets with MRN/DOB columns). These should be **moved out
  of the folder entirely**, not just ignored — see below.
- **MEDIUM** — content that looks like PHI (SSN patterns, `MRN: 1234567`, DOB lines,
  hardcoded connection strings with passwords), or filenames that look like MRNs.
  Open each one and judge.
- **REVIEW** — file types the scanner can't read (Excel, Access, PDFs, backups).
  You have to open these yourself.

**Prefer moving data out over ignoring it.** A `.gitignore` protects you from
accidents; it does not make the folder a safe place for PHI. Best layout:

```
C:\Users\you\Projects\MyEsapiScript\      ← the repo: code only
C:\Users\you\ProjectData\MyEsapiScript\   ← data stays here, never in the repo
```

If data genuinely must live beside the code, put ALL of it in a single `data/`
subfolder and ignore that whole folder.

## Step 2 — Create the .gitignore

Copy [templates/medical-project.gitignore](../templates/medical-project.gitignore)
into your project folder and rename it to `.gitignore` (note the leading dot, no
extension). Then let the scanner add anything it flagged:

```bash
python tools/phi_scan.py "C:\Users\you\Projects\MyEsapiScript" --update-gitignore
```

Read [mindful-gitignore.md](mindful-gitignore.md) to understand what you just
copied — five minutes now saves an incident later.

## Step 3 & 4 — Initialize and REVIEW

### Path A: GitHub Desktop

1. **File → Add local repository** → choose your folder. Desktop will offer to
   "create a repository here" — accept.
2. In the **Changes** tab you now see every file git intends to track.
   **Read the whole list.** This is the review step. Anything that shouldn't be
   there? Fix the .gitignore (right-click a file → *Ignore file* also works) or
   move the file out. The list should be code, docs, and config — nothing else.
3. Write a summary like `Initial commit` and click **Commit to main**.
4. Click **Publish repository**. **Check "Keep this code private."** Uncheck the
   organization field unless you were told otherwise. Publish.

### Path B: Command line

```bash
cd "C:\Users\you\Projects\MyEsapiScript"
git init

# THE REVIEW STEP — read every filename before staging anything:
git status

# If the list looks wrong, fix .gitignore and re-check. Two useful commands:
git status --ignored          # confirm the data files show up as ignored
git check-ignore -v somefile  # explains WHICH rule ignores (or fails to ignore) a file

# When the list is only code/docs/config:
git add .
git status                    # look one more time at what's staged
git commit -m "Initial commit"

# Create the GitHub repo (PRIVATE) and push in one step:
gh repo create MyEsapiScript --private --source . --push
```

## Step 5 — Make it discoverable (team catalog)

If the project is UCSD work, tag it so the Technical Integration Catalog picks it up:

```bash
gh repo edit yourname/MyEsapiScript --add-topic ucsd-catalog
```

(and add Brian as a read collaborator if it's under your personal account — see the
team guide in the UCSD_ProgrammingLead repo).

## Step 6 — Install the safety nets (recommended)

- **Pre-commit hook** — copies of the scanner run automatically on every commit and
  block the commit if PHI patterns show up in staged files:
  ```bash
  copy templates\pre-commit  MyEsapiScript\.git\hooks\pre-commit
  copy tools\phi_scan.py     MyEsapiScript\tools\phi_scan.py
  ```
- **GitHub Action tripwire** — copy
  [templates/phi-scan-workflow.yml](../templates/phi-scan-workflow.yml) to
  `.github/workflows/phi-scan.yml` in your repo. This runs on every push. It is a
  smoke alarm, not a lock — by the time it fires, the data has already reached
  GitHub — but it catches drift and protects you from collaborators who skipped
  the local steps.

---

## Ongoing habits

- Before every push, glance at `git status` / the Desktop Changes list. New file
  types you don't recognize? Investigate first.
- Never `git add -f` (force-add an ignored file) without thinking hard about why
  the rule exists.
- New kind of data export in the project? Add its pattern to .gitignore the same day.

## If PHI was committed anyway

**Committed but NOT pushed:** you're fine, nothing left your machine — but don't
just delete the file and keep going; it's still in history. Easiest fix before
anything was pushed: `git reset --soft <commit-before-the-mistake>`, remove/ignore
the file, re-commit. Ask Brian or Claude Code for help if unsure.

**Already pushed:** treat it as a potential breach, not an engineering problem.

1. Do **not** try to quietly fix it. Deleting the file in a new commit leaves it
   fully retrievable in history; even a force-push doesn't reliably purge GitHub's
   caches and forks.
2. Make the repo private immediately if it was public.
3. Notify Brian and follow the department's incident process — UCSD Health has
   reporting obligations that start on discovery.
4. History rewriting (`git filter-repo`) and GitHub Support cache purging come
   *after* reporting, not instead of it.
