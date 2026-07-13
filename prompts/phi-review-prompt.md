# Standing prompt: PHI review before creating a repo

Copy the block below into Claude Code (or Claude Desktop with file access) when
you're about to turn a folder into a repository. Replace the path.

**Before you use it, understand one thing:** asking Claude to *read file contents*
means transmitting those contents to the Claude API. Filenames, extensions, and
scanner output are low-risk; the *contents* of a file that might hold PHI are the
very thing we're trying not to transmit. The prompt below is written so Claude
works from the scanner's report and file metadata first, and only opens file
contents when you explicitly say so. Check with Brian about the current status of
UCSD's agreement/policy on sending clinical text to Claude before ever loosening
that.

---

```
I'm about to publish the folder <PATH> as a GitHub repository. Before any git
commands are run, help me make sure no PHI (protected health information) can
reach GitHub. Work in this order:

1. Run the deterministic scanner first:
   python tools/phi_scan.py "<PATH>"
   (it's in the CreatingGithubRepoInstructions repo; copy it if needed).

2. Walk me through every HIGH finding. For each, recommend moving the file out
   of the folder rather than gitignoring it, and tell me exactly where the moved
   files should go.

3. For MEDIUM and REVIEW findings, reason from the FILENAME, extension, size,
   and the scanner's stated reason ONLY. Do not open or read file contents
   unless I explicitly tell you to for a specific file. Group them into:
   "safe to commit", "should be ignored", and "I need to open this myself".

4. Look at the remaining folder structure (names only) and flag anything the
   scanner wouldn't catch: folders named after people, dates that could be
   treatment dates, project names containing patient initials, notes/ or
   scratch/ folders likely to contain free-text about patients.

5. Draft the .gitignore for this project starting from
   templates/medical-project.gitignore, adding project-specific entries from
   steps 2-4. Show it to me for approval before writing it.

6. Only after I confirm the folder is clean: give me the exact git + gh
   commands to create a PRIVATE repository, with a `git status` review step
   before anything is committed, and remind me to tag it with the ucsd-catalog
   topic if it's UCSD work.

Do not run git init, git add, git commit, or gh repo create until I've approved
the .gitignore and the status review.
```

---

## Why this prompt is shaped this way

- **Scanner first, LLM second.** The deterministic pass is reproducible and never
  transmits content; Claude adds judgment on top of its report, not instead of it.
- **Metadata-only by default** (step 3) keeps potentially-PHI content off the wire.
- **Step 4 is the part only an LLM can do** — pattern tools can't know that a
  folder named `JS_reirradiation_notes` is a problem.
- **Explicit approval gates** (steps 5-6) keep the human as the accountable
  decision-maker, which is where compliance responsibility actually lives.
