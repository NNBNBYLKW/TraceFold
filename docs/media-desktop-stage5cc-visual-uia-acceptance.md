# TraceFold Stage 5C-C visual UI acceptance record

Status: initial record. Every row below is `NotRun`. This file is a template for the run that
will be performed against the real product on the current host; it is not evidence that the
acceptance has passed.

- Package under test: `TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64` (this toolkit) and
  the sibling `TraceFold-MediaDesktop-Stage5C-C-win-x64` product.
- Entry: `scripts/run-stage5cc-ui-acceptance.ps1`, started through `Run-Stage5CC-Acceptance.cmd`
  (Windows PowerShell 5.1) or directly with the same arguments.
- No clean virtual machine was used. A clean-VM result is not claimed and `cleanVmStatus` stays
  `NotRun`.
- Only the automated runner may fill an automated row with an evidence reference. Only the user may
  change a manual row to `Passed`, and only after performing the check.
- The previous Task 61 manual conclusions belong to their original scope; they are not copied into
  this record as current `Passed` rows.

## Automated rows

| ID | Check | Status | Evidence |
|---|---|---|---|
| A01 | Product smoke: native window, single instance, offline startup, clean exit, no browser process | NotRun | |
| A02 | Toolkit smoke: manifest, member rows, support/driver trees, contract fixtures, version stamp, product boundary | NotRun | |
| A03 | Startup/layout at the published size | NotRun | |
| A04 | UIA patterns and accessible names | NotRun | |
| A05 | Main chain including MP4/M4A preprocessing | NotRun | |
| A06 | Stale-inspector refresh | NotRun | |
| A07 | Metadata cardinality | NotRun | |
| A08 | Source cleanup after ingest | NotRun | |
| A09 | Keep Both / Replace / Skip decisions | NotRun | |
| A10 | Processing failure and retry | NotRun | |
| A11 | Batch metadata, partial failure and undo | NotRun | |
| A12 | Library filter, naming preview/apply and undo | NotRun | |
| A13 | Taxonomy merge, delete guards and undo | NotRun | |
| A14 | Diagnostics read-only, refresh and cleanup | NotRun | |
| A15 | Single-instance safety | NotRun | |
| A16 | Light/dark visual matrix (`ThemeMatrix Both`) | NotRun | |
| A17 | Keyboard, focus, drawer and dialog reachability | NotRun | |
| A18 | Theme restoration after the matrix | NotRun | |
| A19 | Process and temporary-root cleanup after the run | NotRun | |

## User-owned manual rows

| ID | Check | Status | Evidence |
|---|---|---|---|
| M01 | Launcher window persists, prints the summary and pauses on success, blocked and failure | NotRun | |
| M02 | Screenshot density and spacing are readable at the published sizes | NotRun | |
| M03 | Light and dark readability of the main surfaces | NotRun | |
| M04 | Drawer behaviour and focus are intuitive | NotRun | |
| M05 | Windows folder picker opens where expected | NotRun | |
| M06 | The registered log-folder entry opens the real log folder | NotRun | |
| M07 | Clipboard content is redacted where the product promises it | NotRun | |
| M08 | Icon, taskbar, title and native close behaviour | NotRun | |
| M09 | The product package contains no acceptance payload | NotRun | |
| M10 | No product process or owned temporary root remains after the run | NotRun | |

## Development regression (not acceptance rows)

The development evidence for the package producer, the offline entry and the launcher is recorded
separately in the package result document. It does not fill any row above: the product, support
CLI, driver and desktop layers were not started for it, and a green development gate is not a
substitute for this record.

## Rules for filling this record

1. Automated rows get the runner's evidence path (`summary.json`, `<Scenario>/report.json`) and
   the exit code; a `Blocked` or `Failed` run does not fill a row as `Passed`.
2. Manual rows are filled only by the user. A row stays `NotRun` until the user confirms it.
3. A complete acceptance requires every automated row `Passed` and every manual row confirmed by
   the user; a single-scenario run covers one row at most.
4. A clean-VM run is still not claimed even when the current-host run passes.
