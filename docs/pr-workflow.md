# Giving the PHI tripwire teeth: pull requests + a protected main

The [GitHub Action](../templates/phi-scan-workflow.yml) by itself just paints a
red X after the fact — nothing stops the commit from sitting on `main`. This page
sets up the enforcement version: **nobody (including you) commits straight to
`main`; every change arrives by pull request, and the PR cannot merge until the
PHI scan passes.**

## What this buys, honestly

- A failing scan **cannot land on `main`** — the branch your collaborators clone
  and the catalog links to stays clean.
- Every change gets a built-in pause: the PR's "Files changed" tab is a natural
  moment to spot a data file that snuck in, even when the scanner misses it.
- It catches **drift**: the project that was clean at creation and picked up an
  ARIA export in month six.

And the limit, said plainly: the scan runs *after* your branch is pushed, so a
flagged file **has already reached GitHub** — on the branch, not on main. The
teeth prevent it from being merged and force a cleanup, but a HIGH finding on a
PR is still an incident to handle (see below), not just a build to re-run. The
local scanner and pre-commit hook remain the real prevention; this is
containment plus enforcement.

**Plan caveat:** GitHub only *enforces* branch protection on **private** repos
if the account/org is on a paid plan (Pro/Team/Enterprise). On a free personal
account's private repo you can still set the rule and the red X still shows on
the PR — but GitHub won't physically block the merge button. The workflow below
is still worth following as a convention; ask Brian whether your repo should
live under a UCSD org where enforcement is real.

## One-time setup (per repository)

Prerequisites: the repo already contains `tools/phi_scan.py` and
`.github/workflows/phi-scan.yml` (copy both from this repo — see
[creating-a-repo.md](creating-a-repo.md), step 6). Push at least one commit so
the workflow has run once; that makes the check name visible to the settings UI.

### Point-and-click path

1. On the repo page: **Settings → Branches → Add branch protection rule**
   (on newer UIs: **Settings → Rules → Rulesets → New ruleset**).
2. Branch name pattern: `main`.
3. Check **Require a pull request before merging**. Leave "required approvals"
   at 0 if you work alone — the gate we need is the scan, not a reviewer.
4. Check **Require status checks to pass before merging**, search for and select
   **`phi-scan`**. Also check **Require branches to be up to date** if offered.
5. Check **Do not allow bypassing the above settings** (a.k.a. "Include
   administrators") — the rule should apply to you too.
6. Save.

### Command-line path

```bash
gh api -X PUT "repos/OWNER/REPO/branches/main/protection" \
  -H "Accept: application/vnd.github+json" \
  --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["phi-scan"] },
  "enforce_admins": true,
  "required_pull_request_reviews": null,
  "restrictions": null
}
JSON
```

(`required_pull_request_reviews: null` means a PR is required by the merge rules
above but no human approval count is — right for solo projects. Set it to
`{"required_approving_review_count": 1}` for shared ones.)

## The daily flow

The habit change: `main` is read-only in your head. Work happens on branches.

### Command line

```bash
git switch -c fix-dose-report        # 1. branch (short, descriptive name)
# ... edit, commit as usual ...
git push -u origin fix-dose-report   # 2. push the branch
gh pr create --fill                  # 3. open the PR
gh pr checks --watch                 # 4. wait for phi-scan to go green
# 5. read the "Files changed" tab yourself — the scanner can't judge everything
gh pr merge --squash --delete-branch # 6. merge; branch cleanup included
git switch main && git pull
```

### GitHub Desktop

1. **Branch → New branch**, name it, commit your work to it as usual.
2. **Publish branch**, then click **Create Pull Request** (opens the browser).
3. On the PR page, wait for the **phi-scan** check to turn green and skim
   **Files changed** yourself.
4. Click **Merge pull request**, then delete the branch when offered.
5. Back in Desktop: switch to `main` and **Pull origin**.

Yes — for solo projects you open the PR and merge it yourself two minutes later.
That's not bureaucracy: the two minutes are exactly when the scan runs and you
look at the file list. It also builds the habit that makes shared projects safe.

## When the phi-scan check fails on a PR

1. **Do not merge.** Not even "temporarily". Especially not with a red X.
2. Remember the file is already on GitHub, on your branch. Removing it with a
   new commit on the branch leaves it in the branch's history.
3. If the finding is a false positive (e.g. a test fixture with fake MRNs):
   adjust the file or the scanner rules, push, let the check re-run. Mention it
   to Brian so the rules improve.
4. If it's real PHI: follow the incident steps in
   [creating-a-repo.md](creating-a-repo.md#if-phi-was-committed-anyway) —
   report first. Cleanup usually means rebuilding the branch without the bad
   commit (or closing the PR and deleting the branch) *after* reporting;
   deleted branches remain retrievable on GitHub's side until purged, which is
   part of why reporting comes first.
