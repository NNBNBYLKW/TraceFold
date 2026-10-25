# TraceFold Current Release Capability Matrix

[简体中文](capability-matrix.md) | [Back to README](../README.en.md)

This page describes only the current public prerelease, `v4.0.0-stage5cc`. The public product was built from source identity `01264b7a086aebfcfa2f546d294def5076113e3e`; source on the default branch and unpublished builds do not define the behavior of the current public product.

## Shipped scope

| Capability | Current public prerelease | Notes |
| --- | --- | --- |
| Windows desktop media organization | Available | Windows 11 24H2 (build 26100) or later, x64. |
| Inbox previews for photos and ordinary videos | Available | Requires separate Inbox, Library, and Work directories. |
| Matching MP4/M4A pairing and preprocessing | Available | Preprocessing does not commit the item; the user must still commit it to the library. |
| Display name, author, series, and content tags | Available | Metadata is autosaved, and display names are separate from filenames on disk. |
| Search, filters, and vocabulary references | Available | Use the documented search and filtering flows for filenames, display names, authors, series, and tags. |
| Conflict comparison and resolution | Available | The documented UI supports skipping, keeping both, or replacing through the conflict flow. |
| Local-file workflow | Available | The documented workflow does not require a cloud service or online account to be configured first. |
| Bundled runtime components | Available | The product package includes .NET, Windows App SDK, and FFmpeg/FFprobe. |

## Known scope and limits

| Item | Current state | What users and AI assistants should understand |
| --- | --- | --- |
| Release maturity | Prerelease, not stable | Do not describe this build as a stable release. |
| Automated UI acceptance | 7 passed, 5 failed, 2 blocked | Failures and blocked checks were not rewritten as passing because manual checks succeeded. |
| Visual matrix | Full coverage not produced | Do not claim every light/dark and window-size combination passed. |
| Clean virtual machine | `NotRun` | Do not claim clean-system compatibility validation is complete. |
| Project license | Not declared | Do not describe project licensing as confirmed or treat third-party licenses as TraceFold's project license. |

## Not included in the current public product

The following are not shipped capabilities of `v4.0.0-stage5cc` and must not be described as currently available:

- A public API, CLI, or MCP integration for third parties to control the desktop application.
- PDF, EPUB, or comic reading, or ingesting standalone music files and providing a music player/library. Ordinary videos and their audio tracks are not a standalone music library.
- Multiple media spaces or multi-instance workflows.
- OCR, audio transcription, face matching, or automatic tagging.
- Hiding missing library files or manually removing stale records; these are not part of the current Release.
- Using the UI acceptance toolkit as the ordinary user's runtime.

Plans and research notes are not implemented features. If a later release adds any of these capabilities, its public documentation, version identity, this matrix, and actual acceptance evidence must be updated together.
