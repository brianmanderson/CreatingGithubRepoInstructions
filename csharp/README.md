# PHI Scan — desktop GUI

A point-and-click version of [tools/phi_scan.py](../tools/phi_scan.py) for
colleagues who don't use Python or a terminal. Same detection rules, same
.gitignore behavior — the two are kept in lockstep deliberately (see the note at
the top of [Scanner/PhiScanner.cs](PhiScanGui/Scanner/PhiScanner.cs)).

The workflow: Browse to a folder → **Scan** → findings appear color-coded
(HIGH red / MEDIUM yellow / REVIEW blue) → HIGH rows come pre-checked →
**Add checked items to .gitignore** (with a confirmation preview) or
**Open in Explorer** to move the file out — which is always the better fix.

Findings already covered by the folder's .gitignore (e.g. a DICOM file inside a
`Data/` folder you've already ignored) show a **✓ ignored** mark, appear grayed
out, and aren't pre-checked — git can't pick them up, so no action is needed.
Two caveats the app also states: .gitignore does not untrack files git already
tracks, and an ignored folder is still not a safe long-term home for PHI.

## For users: getting it

Download `PhiScanGui.exe` from the repo's
[Releases page](https://github.com/brianmanderson/CreatingGithubRepoInstructions/releases)
(or grab it from the department share). No installation — it targets .NET
Framework 4.8, which is already on every Windows 10/11 machine. Double-click
and go. Windows SmartScreen may warn on first run because the exe is unsigned;
"More info → Run anyway" is expected for an internal tool from this repo.

Run it **before** you turn a folder into a git repository. If it finds things,
prefer moving them out of the folder over ignoring them; the .gitignore button
is the backstop, and it reminds you that ignoring does not untrack or un-push
anything.

## For maintainers: building it

```powershell
dotnet build csharp/PhiScanGui/PhiScanGui.csproj -c Release
# output: csharp/PhiScanGui/bin/Release/net48/PhiScanGui.exe (~35 KB)
```

Any .NET SDK ≥ 6 on Windows can build it; there are no NuGet dependencies.
Ship `PhiScanGui.exe` (the `.config` beside it is optional boilerplate).

**Cutting a release:** bump `<Version>` in the csproj, then push a version tag —

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The [release workflow](../.github/workflows/release.yml) builds the exe on a
GitHub runner and attaches `PhiScanGui.exe` + `PhiScanGui.zip` to the Release
automatically. Creating a Release in the GitHub UI works too — the workflow
attaches the binaries to it either way.

## Changing detection rules

The rules live in two places that MUST stay identical:

- [PhiScanGui/Scanner/PhiScanner.cs](PhiScanGui/Scanner/PhiScanner.cs) (this app)
- [tools/phi_scan.py](../tools/phi_scan.py) (CLI, pre-commit hook, GitHub Action)

If you add an extension or a regex to one, add it to the other in the same
commit. The Python file is the reference implementation; the GitHub Action and
pre-commit hook run it, so a rule that exists only in the GUI protects nobody
in CI.

Architecture, for whoever inherits this: WPF, MVVM-lite
([ViewModels/MainViewModel.cs](PhiScanGui/ViewModels/MainViewModel.cs) holds all
logic; the window has no code-behind beyond `InitializeComponent`). Scanning
runs on a background task with cancellation; the engine
([Scanner/](PhiScanGui/Scanner)) has no WPF dependencies, so it can be reused
from a console tool or ESAPI script as-is.
