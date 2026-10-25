# TraceFold Offline Quick Start

[简体中文](offline-quick-start.md) | [Back to README](../README.en.md)

Use this page on the target computer after the current public product package has already been obtained. It does not replace the download step and does not treat the UI acceptance toolkit (K) as the ordinary product.

The product UI in the current build is in Chinese. English labels in parentheses below explain the actual Chinese controls; they are not additional UI language options.

## Prepare before going offline

1. On a device with network access, download the product ZIP from the [v4.0.0-stage5cc Release](https://github.com/NNBNBYLKW/TraceFold/releases/tag/v4.0.0-stage5cc):
   `TraceFold-MediaDesktop-Stage5C-C-win-x64.zip`
2. Also download `SHA256SUMS.txt` and `ACCEPTANCE-SUMMARY.zh-CN.md` if needed, and save this page for offline reference.
3. Confirm that the target is Windows 11 24H2 (build 26100) or later on x64.
4. Transfer the complete ZIP; do not omit adjacent product dependencies or the `tools` directory after extraction.

## Optional: verify the file

Run this in PowerShell:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:\Downloads\TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
```

Compare the result with the matching entry in `SHA256SUMS.txt`. If they differ, do not use that copy.

## First offline use

1. Extract the complete product ZIP to a local directory.
2. Run `TraceFold.Media.exe` from the extracted directory. Do not copy only the EXE and do not run it from an archive preview.
3. Open **设置 → 文件夹与扫描** (Settings → Folders and scanning) and configure three non-overlapping directories:
   - **待入库目录** (Inbox)
   - **正式文件库目录** (Library)
   - **工作目录** (Work)
4. Select **保存设置** (Save settings).
5. Start with a few **copies** of media in the inbox, then select **立即刷新** (Refresh now) in **待入库** (Inbox).
6. Check the preview and metadata. For matching media that requires preprocessing, finish **开始前置处理** (Start preprocessing), then select **正式入库** (Commit to library).
7. Check the result in the **正式文件库** (Library), and use search or filtering to confirm that metadata can be read back.

## Data notes

- A successful commit removes the corresponding inbox source files; committing a prepared pair also removes the original MP4/M4A inputs and processing output.
- Keep an independent backup outside the three configured directories on first use.
- Do not delete the application database, work directory, or recovery records to reset a state. Record the complete message first, then consult the saved or bundled troubleshooting instructions.
- Application data is stored under `%LOCALAPPDATA%\TraceFold\MediaDesktop` by default. If a custom data root is in use, follow the path shown in **设置 → 系统诊断** (Settings → Diagnostics).

## Offline boundary

- GitHub links, online issues, and re-downloads require network access.
- The current documentation does not declare that an online account is required for the local workflow.
- This build remains a prerelease; the full visual matrix and clean-VM validation are not complete.
- If startup fails, record the complete error text, time, downloaded asset name, and Windows version, then follow [Support (Chinese)](../SUPPORT.md) when network access is available.
