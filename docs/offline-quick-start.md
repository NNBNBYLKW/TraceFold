# TraceFold 离线快速指南

[English](offline-quick-start.en.md) | [返回 README](../README.md)

本页用于已经把当前公开产品包带到目标电脑后，快速完成首次本地使用。它不能代替下载网络，也不把 UI 验收工具包（K）当作普通产品。

## 离线前准备

1. 在有网络的设备上，从 [v4.0.0-stage5cc Release](https://github.com/NNBNBYLKW/TraceFold/releases/tag/v4.0.0-stage5cc) 下载产品 ZIP：
   `TraceFold-MediaDesktop-Stage5C-C-win-x64.zip`
2. 建议同时下载 `SHA256SUMS.txt` 和 `ACCEPTANCE-SUMMARY.zh-CN.md`，并保存本页供离线查阅。
3. 确认目标电脑为 Windows 11 24H2（build 26100）或更新版本、x64。
4. 把完整 ZIP 带到目标电脑；不要漏掉产品包内依赖或 `tools` 目录。

## 可选：核对文件

在 PowerShell 中运行：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:\Downloads\TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
```

将结果与 `SHA256SUMS.txt` 中同名文件的值比较。不一致时不要继续使用该副本。

## 首次离线使用

1. 完整解压产品 ZIP 到一个本地目录。
2. 运行解压目录内的 `TraceFold.Media.exe`；不要只复制 EXE，也不要从压缩包预览中运行。
3. 打开 **设置 → 文件夹与扫描**，配置三个互不包含的目录：
   - 待入库目录
   - 正式文件库目录
   - 工作目录
4. 点击 **保存设置**。
5. 先放入少量媒体**副本**，在 **待入库** 点击 **立即刷新**。
6. 检查预览和资料；需要配对处理的素材先完成 **开始前置处理**，处理完成后再点击 **正式入库**。
7. 到 **正式文件库** 核对结果，并使用搜索或筛选确认资料已经读回。

## 数据注意事项

- 成功入库会清理对应的待入库源文件；配对素材成功入库后也会清理原 MP4/M4A 和处理输出。
- 首次使用必须保留三个配置目录之外的独立备份。
- 不要为了重置状态而删除应用数据库、工作目录或恢复记录。先记录完整提示，再查看随包或预先保存的排障说明。
- 默认应用数据位于 `%LOCALAPPDATA%\TraceFold\MediaDesktop`；自定义数据根时，以“设置 → 系统诊断”显示的路径为准。

## 离线边界

- GitHub 链接、在线 Issue 和重新下载都需要网络。
- 当前文档没有声明产品需要在线账号才能完成本地工作流。
- 当前版本仍是预发布；完整视觉矩阵和干净虚拟机验证未完成。
- 如果启动失败，请记录完整错误文字、发生时间、下载附件名和 Windows 版本，联网后再按 [支持说明](../SUPPORT.md)反馈。
