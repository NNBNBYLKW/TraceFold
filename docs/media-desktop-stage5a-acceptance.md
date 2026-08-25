# TraceFold Media Desktop Stage 5A Acceptance

## Decision and scope

Stage 5A is accepted. The user reported every item in the 13-item manual
checklist as **通过** on 2026-08-26 (Asia/Shanghai); the controller captured
`2026-08-26T02:09:14+08:00` immediately after that report. No exception,
failure, or specification deviation was reported.

The manual run was performed on the user's existing Windows 11 x64 machine.
No clean Windows VM was available. This is a documented environment
limitation, not a substitution of the manual checklist or a reported failure.
Automated/package evidence below is kept distinct from this user-reported
manual result.

The feature branch is `codex/native-media-desktop-stage5a-batch-intake`. Its
merge base is `c9599690da4c52e44277a30f028bc92aaa12a9a7`. The final functional
and packaged source commit is
`9dde268ab6b7293bdea772876ffe6da5a02c81ab`; this acceptance-document commit
follows that source commit and records evidence only—it does not change the
package or product behavior.

## User-reported manual acceptance

Source: the user's explicit complete-checklist result, interpreted against
`docs/media-desktop-stage5a-test-guide.md`. Every item is reported passed:

| # | Checklist item | Result |
| ---: | --- | --- |
| 1 | Offline native-window and single-instance startup | 通过 (user-reported) |
| 2 | Scan test fixtures | 通过 (user-reported) |
| 3 | Click/Ctrl/Shift/Ctrl+A/Esc selection and clearing | 通过 (user-reported) |
| 4 | Filter/refresh and ID-only selection retention | 通过 (user-reported) |
| 5 | Batch metadata add, remove, and replace | 通过 (user-reported) |
| 6 | Disabled replacement fields and explicit clear | 通过 (user-reported) |
| 7 | Atomic metadata automated-evidence check | 通过 (user-reported) |
| 8 | Generic preprocessing and serial FFmpeg | 通过 (user-reported) |
| 9 | Mixed batch ingest and retained conflict | 通过 (user-reported) |
| 10 | Stop after the current item | 通过 (user-reported) |
| 11 | Show-incomplete focus and filter restoration | 通过 (user-reported) |
| 12 | Close during execution and restart recovery | 通过 (user-reported) |
| 13 | No residual application or FFmpeg process after exit | 通过 (user-reported) |

## Automated verification

The final offline Release verification used the following commands:

```powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
git diff --check c959969...HEAD
```

Result: the Release build completed with 0 warnings and 0 errors; all 558
tests passed, with no failures or skips; the base-to-HEAD whitespace check had
no output.

| Project | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| `TraceFold.Media.Domain.Tests` | 42 | 0 | 0 | 42 |
| `TraceFold.Media.Application.Tests` | 196 | 0 | 0 | 196 |
| `TraceFold.Media.Infrastructure.Tests` | 192 | 0 | 0 | 192 |
| `TraceFold.Media.Architecture.Tests` | 6 | 0 | 0 | 6 |
| `TraceFold.Media.Desktop.Tests` | 122 | 0 | 0 | 122 |
| **Total** | **558** | **0** | **0** | **558** |

Focused current-HEAD evidence was also rerun:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~BatchMetadataPersistenceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~BatchProcessingServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~BatchIngestServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~Stage5ALoggingPrivacyTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release --no-build --no-restore --disable-build-servers
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --filter "FullyQualifiedName~Stage5APackageProbeTests|FullyQualifiedName~Stage4PackageProbeOptionsTests|FullyQualifiedName~PackageProbeRefactorTests"
```

Results, in command order: 8/8 atomic metadata tests passed (including stale
version rollback, post-first-chunk trigger rollback, and 600 items); 8/8
batch-processing tests passed; 6/6 batch-ingest tests passed; 1/1 Stage 5A
privacy test passed; 6/6 architecture tests passed; and 30/30 Stage 5A/Stage
4/package-probe regression tests passed. All focused runs had 0 failures and
0 skips.

## Requirement matrix

| Requirement | Evidence |
| --- | --- |
| No second batch state machine or persistence table | `ArchitectureTests.Stage5A_desktop_does_not_know_specific_processor_or_batch_rows` rejects `Mp4M4a`, `BatchJobRow`, and `BatchIngestOperationRow`; architecture run: 6/6. |
| No batch conflict-dialog cascade | `InboxViewModelTests.Confirmed_batch_ingest_retains_only_unfinished_ids` verifies one summary confirmation and zero per-item conflict dialogs; manual item 9 passed. |
| No automatic processing or ingest | `BatchProcessingServiceTests.Start_calls_the_single_item_command_only_for_actionable_items_in_order` delegates only after an explicit batch-start action; `BatchIngestServiceTests.Preview_reclassifies_current_facts_without_calling_ingest` proves preview does not ingest. |
| One series field with explicit semantics | `BatchMetadataMutationTests.Replace_can_explicitly_clear_series_without_touching_other_fields` and `BatchMetadataDialogViewModelTests.Explicit_clear_builds_clear_while_disabled_field_stays_unchanged`; manual item 6 passed. |
| No batch display-name or physical-filename change | Batch metadata command/model tests cover authors, series, and tags only; manual item 5 explicitly inspected both display and physical filenames and passed. |
| No sensitive batch logging | `Stage5ALoggingPrivacyTests.Batch_logs_keep_counts_and_codes_without_private_values` permits counts/codes and rejects path, display name, taxonomy values, and hashes; focused run: 1/1. |

The Stage 5A package-probe regression additionally covers guarded parsing,
atomic metadata, generic processing, partial ingest, conflict retention, and
clean internal storage; its focused run passed 30/30. Stage 4 behavior remains
in that same regression filter rather than relying on an unverified historic
count.

## Package and smoke evidence

The published offline Windows x64 package is
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

The package smoke command was:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage5A-win-x64' -StageCheckpoint 5A
```

Its output was:

```text
PASS: native window, single instance, offline startup, and clean exit verified
PASS: Stage 5A batch metadata, processing, partial ingest, and clean storage verified
```

Package hygiene inspection found no `.pdb`, `.cs`, `.csproj`, `.db`, `.log`,
or `.dmp` files. The post-smoke residual-process query for `TraceFold.Media`
and `ffmpeg` returned no processes.

## Evidence sources and disposition

This record uses the user report and manual guide for manual acceptance;
current-HEAD focused test commands for targeted behavior; the Release
build/full-suite/diff checks for branch verification; and the promoted package
manifest, inventory, hashes, smoke output, hygiene query, and process query
for package evidence. It does not claim a clean-VM run, does not republish or
modify `dist`, and does not treat an automated result as a substitute for the
user's manual result.
