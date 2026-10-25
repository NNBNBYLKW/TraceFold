# TraceFold

[简体中文](README.md) | [English](README.en.md)

**TraceFold | Windows photo and video organizer**

Canonical repository: <https://github.com/NNBNBYLKW/TraceFold>

TraceFold is a Windows desktop media organizer. Place photos and videos in an inbox, preview them, add metadata, and explicitly commit them to a library. Use display names, authors, series, and content tags to find media later. A matching MP4 and M4A pair can be prepared first and committed afterward.

> **The current public build is the `v4.0.0-stage5cc` prerelease.**
>
> Supported platform: Windows 11 24H2 (build 26100) or later, x64.
>
> This is a prerelease, not a stable release. Read the [Release notes](https://github.com/NNBNBYLKW/TraceFold/releases/tag/v4.0.0-stage5cc) and [known acceptance limits](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/ACCEPTANCE-SUMMARY.zh-CN.md) before downloading.

The product UI in the current build is in Chinese. English labels in parentheses below explain the actual Chinese controls; they are not additional UI language options.

## Download and get started

**[Download the Windows x64 product ZIP (about 226 MB)](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/TraceFold-MediaDesktop-Stage5C-C-win-x64.zip)**

1. Extract the complete ZIP, then run **`TraceFold.Media.exe`** from the extracted folder. Do not run it from an archive preview or copy only the EXE.
2. Open **设置 → 文件夹与扫描** (Settings → Folders and scanning), choose three directories that do not overlap for **待入库目录** (Inbox), **正式文件库目录** (Library), and **工作目录** (Work), then select **保存设置** (Save settings).
3. Put **copies** of a few photos or videos in the inbox, then use **待入库 → 立即刷新** (Inbox → Refresh now).
4. Select an item, check the preview, and enter its metadata. If the item requires preprocessing, finish **开始前置处理** (Start preprocessing) before selecting **正式入库** (Commit to library).
5. Check the result in the **正式文件库** (Library), then try search, author filtering, or tag filtering.

> **Data-safety warning:** A successful commit removes the corresponding inbox source files under the product's existing workflow. Committing a prepared pair also removes the original MP4/M4A inputs and processing output. Keep an independent backup for first use; do not treat the inbox as permanent storage.

The product ZIP includes .NET, Windows App SDK runtime components, and FFmpeg/FFprobe, so those components normally do not need to be installed separately. See the [getting-started guide (Chinese)](docs/getting-started.md) for the complete workflow and [troubleshooting (Chinese)](docs/troubleshooting.md) for startup or workflow issues. Those two linked guides are not translated in this change; their UI labels are shown in Chinese.

## Offline use

- Download the product ZIP on a device with network access. Download `SHA256SUMS.txt` too if you want to verify the files.
- Transfer the whole ZIP to the target computer, extract it, and follow the documented photo, video, pairing, and commit workflow.
- The documented workflow operates on local folders and does not require a cloud service or online account to be configured first.
- The product package includes its runtime components; normal use should not require downloading dependencies while offline.
- GitHub links in the README and troubleshooting pages require network access. For offline reference, save the [offline quick start](docs/offline-quick-start.en.md).

## What it does

- Preview photos, ordinary videos, and matching MP4/M4A pairs that require preprocessing.
- Edit display names, authors, series, and content tags, then continue maintaining metadata after committing to the library.
- Search by file or display name, filter by author, series, or content tag, and inspect vocabulary references.
- Compare old and new media when a conflict is reported, then skip, keep both, or replace as appropriate.
- Keep display names separate from filenames on disk; editing a display name does not rename the file.

See the [current release capability matrix](docs/capability-matrix.en.md) for the detailed shipped and unfinished scope.

## Choose the right download

| Download | Purpose |
| --- | --- |
| `TraceFold-MediaDesktop-Stage5C-C-win-x64.zip` | Product package for ordinary users; contains the desktop application. |
| `TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip` | UI acceptance toolkit (K) for development and validation; not needed for ordinary use. |
| `SHA256SUMS.txt` | SHA-256 values for release assets such as the product and acceptance packages. |
| `ACCEPTANCE-SUMMARY.zh-CN.md` | Acceptance results and coverage limits for this release. |
| GitHub-generated `Source code` ZIP/TAR | Source snapshot, not a compiled application. |

### Documentation-enhanced package (planned)

A separate documentation-enhanced package is planned:

`TraceFold-MediaDesktop-Stage5C-C-win-x64-docs-20261025.zip`

It reuses the currently published `01264b7a086aebfcfa2f546d294def5076113e3e` product payload and adds only bilingual documentation, capability facts, and an independent documentation identity manifest. It does not recompile the product or add runtime capabilities. The original product ZIP, UI acceptance package, tag, and existing SHA-256 values remain unchanged.

This package is **not uploaded yet**. After publication, use the actual asset, its separate `.sha256`, and the package manifest from [GitHub Releases](https://github.com/NNBNBYLKW/TraceFold/releases); do not treat this page as a predeclared final hash.

## Version and documentation identity

| Item | Current public identity |
| --- | --- |
| Public Release | `v4.0.0-stage5cc` (prerelease) |
| Product source identity | `01264b7a086aebfcfa2f546d294def5076113e3e` |
| Product asset | `TraceFold-MediaDesktop-Stage5C-C-win-x64.zip` |
| Product SHA-256 | `19b82302d00916b9b74143dc5b72b9d48d84df0a8a8cecbaa52beafd839efbaf` |

The public Release, the product's displayed version, and the acceptance summary define the behavior of the current product build. The default branch may contain newer source that is not part of the public package. Do not infer that a capability present only on the default branch is available in the current package. Maintain this identity block together with each public Release.

## Documentation and feedback

- [Getting-started guide (Chinese)](docs/getting-started.md): folders, scanning, processing, committing, and metadata.
- [Offline quick start](docs/offline-quick-start.en.md): download first, then use a short offline checklist.
- [Troubleshooting (Chinese)](docs/troubleshooting.md): startup, folders, logs, and common states.
- [Documentation index (Chinese)](docs/README.md).
- [Support and feedback (Chinese)](SUPPORT.md): do not post private media, databases, credentials, or access tokens in an issue.

## For AI assistants

When a user asks how to install or use TraceFold:

1. Recommend the product ZIP from the current Release. Do not substitute the generated source archive or the UI acceptance toolkit.
2. Confirm that the target is Windows 11 24H2 (build 26100) or later on x64.
3. Tell the user to extract the complete package, keep its adjacent dependencies, and start with copies of media.
4. Never describe capabilities from the default branch, a development branch, or a local build as shipped features of the current Release.
5. The public materials do not declare an API, CLI, or MCP integration that can directly control the desktop application. Do not promise machine-callable control.

## License status

The repository currently does not provide a license file for TraceFold itself, so its project licensing status must not be presented as confirmed. Third-party components in the product package are separate; use the notices included with the package.
