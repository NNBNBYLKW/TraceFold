# TraceFold Stage 5C-C visual UI acceptance - operator guide

This guide describes what the offline toolkit in this package can do today, how to run one
scenario from the package root, where the results land, and what is not covered here.
It describes only capabilities that exist in the packaged scripts; it does not invent theme,
journal or recovery commands.

## 1. What this package is

The package is a moved toolkit: it carries the public entry, its modules and scenarios, the report
template, the pinned winapp driver, a self-contained support executable, the contract and its
fixtures, and `toolkit.json`, which binds every file by size and SHA-256. Nothing is compiled,
downloaded or installed when it runs.

```
Run-Stage5CC-Acceptance.cmd        <- start here (package root)
scripts/Run-Stage5CC-Acceptance.ps1    launcher helper (fixed Windows PowerShell 5.1)
scripts/run-stage5cc-ui-acceptance.ps1 public entry (real runner)
scripts/ui-acceptance/...               modules, scenarios, report template
runtime/support/                        self-contained support executable
runtime/driver/winapp.exe               pinned driver 0.5.0
apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json
toolkit.json                            member table (path, bytes, sha256, source)
docs/                                   this guide and the acceptance record
```

The product package is a separate sibling directory, normally
`TraceFold-MediaDesktop-Stage5C-C-win-x64` next to the toolkit root.

## 2. Prerequisites

- Windows 10/11 x64 with Windows PowerShell 5.1 at
  `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`. The launcher uses that absolute
  path; it does not use PowerShell 7 and does not search for another host.
- An unpacked product directory. It is either the sibling directory above or an explicit path.
- No .NET SDK, network access or installer is needed: the toolkit is self-contained.
- Paths may contain spaces or non-ASCII characters; pass them quoted, exactly as usual.

## 3. Modes

### 3.1 Complete acceptance mode

The complete mode means every registered scenario (`All`) plus the light/dark theme matrix
(`-ThemeMatrix Both`). The launcher forwards both words to the public entry; the entry's `Visual`
scenario runs two real rounds - apply a theme, observe the matrix, restore the original value and
read it back - through the reviewed support command surface.

```
Run-Stage5CC-Acceptance.cmd
```

Nothing is dropped silently: `Both` is a real parameter, not an empty placeholder, and a run that
cannot apply, observe or restore a theme reports `Blocked`/`Failed` instead of a partial pass.
How real the run is depends on what it is pointed at: the packaged product, the pinned driver and
an interactive desktop. The visual matrix was **not executed** while this development package was
built; see section 6.

### 3.2 One scenario

```
Run-Stage5CC-Acceptance.cmd -Scenario StartupLayout
```

Optional arguments:

```
-ProductDirectory "D:\packages\TraceFold-MediaDesktop-Stage5C-C-win-x64"
-OutputDirectory  "D:\runs\startup-20261023"
```

- `-Scenario` must be one name from the entry's real registry (`StartupLayout`, `MainChain`,
  `ConflictKeepBoth`, ... `Visual`). `All` is refused with the complete-mode message.
- `-ProductDirectory` defaults to the sibling product directory. A relative path is resolved
  against the current directory; the package itself is always located from the launcher's own
  path, so the caller's working directory never decides which toolkit runs.
- `-OutputDirectory` must be a fresh leaf. It defaults to
  `<toolkit>\results\run-<utc timestamp>`; an existing directory is refused instead of being
  overwritten.

The launcher passes exactly these arguments to the packaged entry:

```
<system PS5.1> -NoProfile -File <toolkit>\scripts\run-stage5cc-ui-acceptance.ps1
    -ToolkitDirectory <toolkit> -ProductDirectory <product>
    -OutputDirectory <fresh leaf> -Scenario <name>
```

A single scenario is not a complete acceptance: it covers one scenario only and must not be
reported as the Task 14 acceptance result.

## 4. What happens and what you see

The launcher prints the package, product, results leaf and scenario before starting. When the
entry returns it reports the exit code and, only if `summary.json` actually exists and carries a
readable status, that status. Two different outcomes are reported separately: no summary file was
generated at all, or the summary exists but its status could not be read.

- Exit code `0`: the requested scenario passed (a readable summary says `Passed`).
- Exit code `1`: the entry finished with `Blocked`, `Failed` or `NotRun` and wrote a summary; read
  it when the launcher reports one. A host, parameter-binding or unhandled error can also end
  non-zero without a summary.
- Exit code `2`: the entry body refused the arguments (for example an unknown scenario name that
  reaches its own resolver). An unknown name can also be rejected by the host's parameter binding
  before the body runs; that path has a different code and may write no summary.
- Exit code `4`: a pre-launch problem (missing entry, product directory, results leaf or
  PowerShell 5.1).

`Blocked` means the run could not start the thing it needed (for example the product executable or
the packaged driver was unavailable). `Failed` means a scenario ran and did not meet its checks,
including a failure while the run was being cleaned up. `NotRun` means the scenario was never
dispatched. The summary's `status` is authoritative; the launcher only reports it when the file is
there.

When no status is reported, the launcher separates the two cases below and exits with the native
code in both.

- `No summary was generated; read the console output above.` - `summary.json` does not exist. That is
  a normal outcome for a refusal that happens before the entry body writes anything, and for errors
  raised by the host itself.
- `The summary exists at <path> but its status could not be read; read the console output above.` -
  the file is on disk but is empty, truncated, not valid JSON, or carries no usable `status`. The
  launcher still lists it under `Generated:` because it is really there; read the console output to
  find out what happened.

The console always pauses on every terminal state, including a pre-launch error, so it cannot
flash and disappear. Press Enter to close it.

## 5. Results

`summary.json` is written at the results leaf. `host-condition.json` is written at the same leaf
when the run produces it. The scenario's own evidence lives in `<leaf>\<Scenario>\`: a report at
`<Scenario>\report.json`, and `screenshots.json` / `index.html` when that scenario produced them.
The launcher inspects only the selected scenario's directory and prints a `Generated:` line only
for files that are actually present there; it does not search the tree.

No browser is opened automatically. To inspect screenshots, open `<leaf>\<Scenario>\index.html`
manually when the launcher reported it. A missing `Generated:` line means the launcher did not see
that file at that location; it is not a statement about anything outside the selected scenario.

## 6. Theme, journal and recovery - current facts

- The Visual scenario (`70-visual-keyboard-matrix.ps1`) drives the light/dark matrix through the
  reviewed support theme command: each round applies a mode, observes the matrix, and restores the
  original value in a `finally` with a readback. `CurrentOnly` writes no theme, records the
  missing mode as `NotRun`, and a leftover journal is classified before anything starts.
- The journal is the per-user `theme-recovery.json`; a private journal is only accepted together
  with a fixture root and its explicit licence. The startup gate runs before any product check and
  reports `Blocked` when it cannot answer.
- The theme a run observed is the app theme it applied, not a claim about what the user sees now;
  a restore that cannot be confirmed blocks the next round and keeps the journal.
- There is no separate recovery entry point: the startup gate is the only place that resolves an
  interrupted journal, and `summary.json` plus the per-scenario report are evidence only.
- The entry writes only inside its own results leaf and its owned run root; it does not change
  product files.

## 7. What is not covered here

- The real product, the real support CLI, the real pinned driver and a real desktop session are
  not started by the development evidence for this package. Those layers remain `NotRun`.
- The full `All` run and the theme matrix are implemented and reachable in code, but were not
  executed while this development package was built; no matrix result is claimed here.
- No clean virtual machine was used, and no clean-VM result is claimed.
- The acceptance record in `docs/media-desktop-stage5cc-visual-uia-acceptance.md` starts with every
  row `NotRun`; only a user can mark the manual rows, and only after performing them.
