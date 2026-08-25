# TraceFold Media Desktop Stage 5A Acceptance

## Decision and scope

Stage 5A is **pending targeted manual retest** for the current branch and its
next package. Product/UI code changed after the earlier accepted package: the
batch-processing orchestrator now converts an unexpected single-item command
exception into stable per-item results, and running batch ingest now shows an
explicit one-based current/total position. The prior acceptance does not
certify this later binary.

Historically, the user reported every item in the 13-item manual checklist as
**通过** on 2026-08-26 (Asia/Shanghai) for the package sourced from
`9dde268ab6b7293bdea772876ffe6da5a02c81ab`; the controller captured
`2026-08-26T02:09:14+08:00` immediately after that report. No exception,
failure, or specification deviation was reported for that historical package.

The manual run was performed on the user's existing Windows 11 x64 machine.
No clean Windows VM was available. This is a documented environment
limitation, not a substitution of the manual checklist or a reported failure.
Automated/package evidence below is kept distinct from this user-reported
manual result.

The feature branch is `codex/native-media-desktop-stage5a-batch-intake`. Its
merge base is `c9599690da4c52e44277a30f028bc92aaa12a9a7`. The latest package
facts will be recorded after the functional fix commit is published offline
and checked. Until items 8, 9, and 10 are rerun on that package, the current
branch remains pending targeted retest.

## Manual acceptance state

Source for the historical result: the user's explicit complete-checklist
result for source `9dde268`, interpreted against
`docs/media-desktop-stage5a-test-guide.md`. The current branch retains that
history but requires targeted retest of the affected items:

| # | Checklist item | Historical `9dde268` package | Current branch/package |
| ---: | --- | --- | --- |
| 1 | Offline native-window and single-instance startup | 通过 (user-reported) | Historical pass retained; unaffected |
| 2 | Scan test fixtures | 通过 (user-reported) | Historical pass retained; unaffected |
| 3 | Click/Ctrl/Shift/Ctrl+A/Esc selection and clearing | 通过 (user-reported) | Historical pass retained; unaffected |
| 4 | Filter/refresh and ID-only selection retention | 通过 (user-reported) | Historical pass retained; unaffected |
| 5 | Batch metadata add, remove, and replace | 通过 (user-reported) | Historical pass retained; unaffected |
| 6 | Disabled replacement fields and explicit clear | 通过 (user-reported) | Historical pass retained; unaffected |
| 7 | Atomic metadata automated-evidence check | 通过 (user-reported) | Historical pass retained; unaffected |
| 8 | Generic preprocessing and serial FFmpeg | 通过 (user-reported) | **Pending targeted retest** |
| 9 | Mixed batch ingest, current/total progress, and retained conflict | 通过 (user-reported) | **Pending targeted retest** |
| 10 | Stop after the current item | 通过 (user-reported) | **Pending targeted retest** |
| 11 | Show-incomplete focus and filter restoration | 通过 (user-reported) | Historical pass retained; unaffected |
| 12 | Close during execution and restart recovery | 通过 (user-reported) | Historical pass retained; unaffected |
| 13 | No residual application or FFmpeg process after exit | 通过 (user-reported) | Historical pass retained; unaffected |

## Automated verification

The current functional-fix Release verification used the following commands:

```powershell
dotnet clean apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --nologo
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
git diff --check c959969...HEAD
```

Result: the clean and Release build completed with 0 warnings and 0 errors;
all 561 tests passed, with no failures or skips; the base-to-HEAD whitespace
check had no output.

| Project | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| `TraceFold.Media.Domain.Tests` | 42 | 0 | 0 | 42 |
| `TraceFold.Media.Application.Tests` | 198 | 0 | 0 | 198 |
| `TraceFold.Media.Infrastructure.Tests` | 192 | 0 | 0 | 192 |
| `TraceFold.Media.Architecture.Tests` | 6 | 0 | 0 | 6 |
| `TraceFold.Media.Desktop.Tests` | 123 | 0 | 0 | 123 |
| **Total** | **561** | **0** | **0** | **561** |

Focused current-HEAD evidence was also rerun:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~BatchMetadataPersistenceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~BatchProcessingServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~BatchIngestServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~Stage4LoggingPrivacyTests|FullyQualifiedName~Stage5ALoggingPrivacyTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release --no-build --no-restore --disable-build-servers
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~InboxBatchSummaryViewModelTests|FullyQualifiedName~InboxViewModelTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~Stage5APackageProbeTests|FullyQualifiedName~Stage4PackageProbeOptionsTests|FullyQualifiedName~PackageProbeRefactorTests"
```

Results, in command order: 8/8 atomic metadata tests passed (including stale
version rollback, post-first-chunk trigger rollback, and 600 items); 9/9
batch-processing tests passed; 7/7 batch-ingest tests passed; 2/2 Stage 4/5A
privacy tests passed; 6/6 architecture tests passed; 26/26 batch summary/inbox
orchestration tests passed; and 30/30 Stage 5A/Stage 4/package-probe regression
tests passed. All focused runs had 0 failures and 0 skips.

## Requirement matrix

| Requirement | Evidence |
| --- | --- |
| No second batch state machine or persistence table | The automated guard is intentionally narrow: `ArchitectureTests.Stage5A_desktop_does_not_know_specific_processor_or_batch_rows` scans only `TraceFold.Media.Desktop` C# sources and rejects the names `Mp4M4a`, `BatchJobRow`, and `BatchIngestOperationRow`; architecture run: 6/6. The whole-branch conclusion comes from review of production and persistence/schema changes in `c959969...HEAD`; that review is not app-wide automated proof. |
| No batch conflict-dialog cascade | `InboxViewModelTests.Confirmed_batch_ingest_retains_only_unfinished_ids` verifies one summary confirmation and zero per-item conflict dialogs. Manual item 9 passed on the historical package and is pending targeted retest on the current package. |
| No automatic processing or ingest | `BatchProcessingServiceTests.Start_calls_the_single_item_command_only_for_actionable_items_in_order` delegates only after an explicit batch-start action; `BatchIngestServiceTests.Preview_reclassifies_current_facts_without_calling_ingest` proves preview does not ingest. |
| One series field with explicit semantics | `BatchMetadataMutationTests.Replace_can_explicitly_clear_series_without_touching_other_fields` and `BatchMetadataDialogViewModelTests.Explicit_clear_builds_clear_while_disabled_field_stays_unchanged`; manual item 6 passed on the historical package. |
| No batch display-name or physical-filename change | Batch metadata command/model tests cover authors, series, and tags only; manual item 5 explicitly inspected both display and physical filenames and passed on the historical package. |
| No sensitive batch logging | `Stage5ALoggingPrivacyTests.Batch_logs_keep_counts_and_codes_without_private_values` permits counts/codes and rejects path, display name, taxonomy values, and hashes. `Stage4LoggingPrivacyTests.Conflict_failure_keeps_ids_and_code_without_private_evidence` injects physical-filename and probe-owner-token markers (with paths, display name, taxonomy values, and hash) into a failure and asserts every marker is absent from captured logs. Focused privacy run: 2/2. Batch dialog-input handling has bounded static evidence rather than a dedicated log-capture test: `WindowsBatchMetadataDialogService` and `WindowsBatchIngestConfirmationService` contain neither an `ILogger` dependency nor a `Log*` call, so those adapters do not emit dialog-input logs. |

The Stage 5A package-probe regression additionally covers guarded parsing,
atomic metadata, generic processing, partial ingest, conflict retention, and
clean internal storage; its focused run passed 30/30. Stage 4 behavior remains
in that same regression filter rather than relying on an unverified historic
count.

### Privacy-evidence boundary

The focused privacy command was:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~Stage4LoggingPrivacyTests|FullyQualifiedName~Stage5ALoggingPrivacyTests"
```

It passed 2/2 with 0 failures and 0 skips. `Stage4LoggingPrivacyTests` is the
specific automated evidence for physical filenames and probe owner tokens.
There is no current automated test that injects a batch-dialog input marker
into a logger and asserts its absence. The dialog-input statement above is
therefore limited to the two batch dialog adapter source files, for which the
following static inspection returned no matches:

```powershell
rg -n "(Log[A-Za-z]*\(|ILogger|Log\.)" apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs -g '*.cs' -g '*.xaml.cs'
```

## Historical accepted package, deployment, and smoke evidence

The previously accepted offline Windows x64 package was
`dist/TraceFold-MediaDesktop-Stage5A-win-x64`; its ZIP is
`dist/TraceFold-MediaDesktop-Stage5A-win-x64.zip`.

| Item | Verified value |
| --- | --- |
| Directory inventory | 579 files; 551,599,795 bytes |
| ZIP size | 225,530,443 bytes |
| ZIP SHA-256 | `9FF7E5A872455C63E2C797F30C6469D90F18AECD43B8A50C018D48F25D3917A4` |
| `package-version.json` checkpoint | `5A` |
| `package-version.json` source commit | `9dde268ab6b7293bdea772876ffe6da5a02c81ab` |
| Informational version | `4.0.0-stage5a+9dde268ab6b7293bdea772876ffe6da5a02c81ab` |
| Bundled FFmpeg version | `2025-06-23-git-e6298e0759-full_build-www.gyan.dev` |
| `tools/ffmpeg/ffmpeg.exe` SHA-256 | `D5A3CDFF713F3DE9C3EC6CD0DD1BCD046BB0EB97712BD32D340E39D1F88DEF05` |
| `tools/ffmpeg/ffprobe.exe` SHA-256 | `E6C17A4AE25E03275ADB00F664448D5D37ADDB88AE82372BAF1DC8BBDC7C3BB2` |

Direct read-only package inspection establishes the deployment form:

```powershell
$package = 'dist\TraceFold-MediaDesktop-Stage5A-win-x64'
$rootExe = Join-Path $package 'TraceFold.Media.exe'
$appx = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -in 'AppxManifest.xml','AppxBlockMap.xml' -or $_.Extension -in '.appx','.msix','.msixbundle','.appxbundle' }
$runtime = @('hostfxr.dll','hostpolicy.dll','coreclr.dll','TraceFold.Media.runtimeconfig.json') | ForEach-Object { $path = Join-Path $package $_; [pscustomobject]@{ Name = $_; Exists = Test-Path -LiteralPath $path; Length = if (Test-Path -LiteralPath $path) { (Get-Item -LiteralPath $path).Length } else { $null } } }
[pscustomobject]@{ RootTraceFoldMediaExe = Test-Path -LiteralPath $rootExe; AppxArtifactsFound = @($appx).Count; AppxArtifactNames = (@($appx | Select-Object -ExpandProperty Name) -join ','); RuntimePayload = $runtime } | ConvertTo-Json -Depth 4
```

Output: root `TraceFold.Media.exe` exists; `AppxArtifactsFound` is `0`; and
the root has `hostfxr.dll` (379,728 bytes), `hostpolicy.dll` (378,664 bytes),
`coreclr.dll` (4,609,320 bytes), and `TraceFold.Media.runtimeconfig.json`
(566 bytes). This is direct evidence of an unpackaged root-EXE deployment with
its self-contained .NET runtime payload, rather than an MSIX/AppX package.

The package smoke command was:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage5A-win-x64' -StageCheckpoint 5A
```

Its output was:

```text
PASS: native window, single instance, offline startup, and clean exit verified
PASS: Stage 5A batch metadata, processing, partial ingest, and clean storage verified
```

The smoke script launches the root EXE and sets `PATH` to only
`$env:SystemRoot\System32` for its restricted-PATH run (`$exePath` at line 13;
PATH restriction at line 239), so the recorded offline PASS result also checks
that the package does not depend on a host FFmpeg/runtime lookup.

Package hygiene inspection found no `.pdb`, `.cs`, `.csproj`, `.db`, `.log`,
or `.dmp` files. The post-smoke residual-process query for `TraceFold.Media`
and `ffmpeg` returned no processes.

## Evidence sources and disposition

This record preserves the user report and package evidence for the historical
`9dde268` package. Automated evidence for the current branch does not replace
the pending manual rerun of items 8, 9, and 10, and the document does not claim
a clean-VM run.

Evidence-verification boundary: the current functional-fix verification above
was run in this worktree. The package inventory, hash, FFmpeg, hygiene/process,
and smoke facts in the historical section still describe only source
`9dde268`; a follow-up documentation commit will record the newly published
package facts without changing product behavior.
