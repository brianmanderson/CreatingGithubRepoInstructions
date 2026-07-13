# Automating PHI detection: the options, and what we chose

The goal: before (and every time) a project folder touches GitHub, something
checks it for PHI — DICOM files, MRN-bearing spreadsheets, hardcoded patient IDs —
and helps route those files into the .gitignore or out of the folder entirely.

Three candidate shapes were on the table. The punchline first:

> **The decisive constraint is *when* the check runs.** A check that runs on
> GitHub's servers runs *after* the data has already left UCSD — at that point the
> incident has occurred and the tool is just the first to notice. Only a check that
> runs on the local machine, before the first commit, actually prevents anything.
> Everything else follows from that.

## Option 1 — A GitHub workflow (Action)

A YAML file in `.github/workflows/` that runs a scanner on every push / pull
request and fails the build if PHI patterns appear.

**For:**
- Zero effort per person once installed; nobody can forget to run it.
- Centralized: update the scanner once, every repo using it benefits.
- Catches *drift* — the project that was clean at creation and picked up a data
  export six months later.
- Also catches collaborators who never read our instructions.

**Against (disqualifying as the primary defense):**
- **It runs after push.** The PHI is already on GitHub's infrastructure when the
  red X appears. For HIPAA purposes the disclosure already happened; the Action
  just converts "silent breach" into "detected breach". Valuable — but it's a smoke
  alarm, not a lock.
- Each repo must carry the workflow file (or the org must enforce it via required
  workflows, which needs a GitHub org — most of our repos are on personal accounts).
- Reading logs requires enough GitHub literacy to notice a failed check.

**Verdict: adopt as the second layer, never the first.**
[templates/phi-scan-workflow.yml](../templates/phi-scan-workflow.yml) is ready to
copy into any repo.

## Option 2 — A shareable compiled tool (C#)

A WPF or console app: point it at a folder, it scans, shows findings, offers to
write the .gitignore. Distributed as a single .exe on the department share.

**For:**
- Runs locally, **before** anything touches git — the right point in time.
- The team writes C# daily; maintainable in-house. Could grow a friendly GUI for
  non-programmers (checkbox list: "ignore these?").
- No runtime prerequisites if published self-contained.

**Against:**
- Distribution/update problem: the .exe on the share goes stale; people run
  year-old copies with year-old patterns. (An auto-update check is more
  infrastructure than this deserves.)
- A GUI tool can't easily *block* a commit — it relies on people remembering to
  run it. The natural enforcement point, a git pre-commit hook, wants a
  command-line tool anyway.
- Harder to reuse the same logic inside the GitHub Action (would need the .exe
  checked in or a build step; awkward next to `python phi_scan.py`).

**Verdict: viable, and the right *future* shape if we decide non-programmers need
a double-click GUI. Not the first build.**

> **Update:** built — see [csharp/](../csharp/README.md). A WPF app (.NET
> Framework 4.8, zero-install on Windows 10/11) that runs the identical rules
> and shares the Python tool's .gitignore managed block. The Python script
> remains the reference implementation; rules change in both files together.

## Option 3 — A prompt for Claude

A standing prompt each person gives Claude Code: "review this folder for PHI
before I create the repo."

**For:**
- Judgment, not just patterns. A regex can't tell that `JS_boost_recurrence.docx`
  is about a patient, or that a "site initials" column is identifying in context.
  An LLM reviewing borderline files catches what pattern-matching structurally
  cannot.
- Zero code to maintain; instructions improve by editing a markdown file.
- Meets people where they already are — the team is adopting Claude anyway.

**Against (disqualifying as the *only* defense):**
- **Non-deterministic.** The same folder can get different reviews on different
  days. Compliance tooling needs the boring property: same input, same output.
- Requires each person to remember to run it, and to have Claude set up.
- **The recursion problem:** having an LLM read potential PHI means transmitting
  that content to the API. Under Anthropic's zero-retention/enterprise terms and
  UCSD policy this may be acceptable, but it must be a deliberate, verified
  decision — not a default. The deterministic scanner has no such question: it
  never leaves the machine.

**Verdict: adopt as the judgment layer** — Claude runs the deterministic scanner,
then helps the human reason about MEDIUM/REVIEW findings (by filename and
metadata first, content only where permitted).
[prompts/phi-review-prompt.md](../prompts/phi-review-prompt.md) is the standing prompt.

## What we built: layered, one shared engine

| Layer | When it runs | What it is |
|---|---|---|
| 1. **Local scan — the primary gate.** Recommended: the [desktop app](../csharp/README.md); equivalently the [Python script](../tools/phi_scan.py). | Before the first commit, on demand | Deterministic scan → report → optional .gitignore update. Both run identical rules. **This is where PHI actually gets stopped.** |
| 2. **Pre-commit hook** ([templates/pre-commit](../templates/pre-commit)) | Every `git commit`, automatically | Runs the Python script; blocks the commit on findings. Enforcement without memory. |
| 3. **GitHub Action** ([templates/phi-scan-workflow.yml](../templates/phi-scan-workflow.yml)) | Every push / PR, on GitHub | Same scanner as tripwire — catches drift and hook-skippers. With [PR + branch protection](pr-workflow.md), it becomes a real gate on `main`. |
| 4. **Claude review** ([prompts/phi-review-prompt.md](../prompts/phi-review-prompt.md)) | On demand, for judgment calls | Human+LLM review of what layers 1–3 flag but can't decide. |

**For a person scanning a folder, the desktop app is the recommended default** —
no Python, no terminal, point-and-click, with color-coded findings and a
preview before it edits your .gitignore. The Python script is the same engine
for people who prefer the terminal, and it's what the automated layers (hook,
Action, Claude) invoke, since those can't open a GUI.

Why a Python core engine underneath, rather than only C#:

1. **One artifact, several contexts.** The identical script runs standalone, in the
   pre-commit hook, in the GitHub Action, and under Claude's supervision. A
   compiled exe fits only some of those cleanly.
2. **Source-visible.** Anyone can open the script and see exactly what patterns it
   checks — auditable compliance beats a black-box binary.
3. **Stdlib-only, no install step** beyond Python itself, which the team's
   ML/DICOM work already requires. Same convention as the catalog bridge script.
4. **The C# GUI is the human-facing front door** onto those same rules (ported
   from, and kept in lockstep with, the script) — so non-programmers get a
   double-click tool while automation keeps the one auditable engine.

## Known limits (say them out loud)

- The scanner is pattern-based: it will miss PHI in free prose ("saw Mr. Smith
  after his 3/2 fraction"), inside images/PDF scans, and in any format it labels
  REVIEW. A clean scan narrows the search; it is not a certification.
- `.gitignore` mitigation is second-best; the guidance everywhere is **move data
  out of the repo folder**.
- None of this replaces the department's actual privacy obligations — it lowers
  the odds of an accident and shortens time-to-detection when one happens.
