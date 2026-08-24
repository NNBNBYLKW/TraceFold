# TraceFold Media Desktop

TraceFold Media Desktop Stage 4B 是原生 Windows 文件入库客户端的中断恢复检查点。它提供：

- 待入库目录的手动刷新，以及默认关闭的自动扫描；
- 图片和普通视频的稳定性判断、预览、资料标注和正式入库；
- 多作者、单系列、多内容标签，以及显示名称与物理文件名分离；
- 正式文件库搜索、详情编辑和受控的“打开所在目录”；
- 同名、同内容及组合冲突检测；原生对比弹窗会展示新旧预览、物理/显示名称、登记状态、媒体属性和标签资料；
- 明确区分关闭/`Esc`、跳过、两者都保留和替换所选文件；冲突可在重启后从右侧详情栏重新打开；
- “两者都保留”自动选择下一可用后缀；“替换”只处理用户选中的一个目标，并为已登记文件保留资产 ID 与物理文件名；
- 受身份校验保护的替换备份、提交和清理；源或目标发生变化时不覆盖、不删除，并要求重新确认；
- 启动时按持久化证据自动继续或恢复被中断的入库；证据矛盾时进入 `RecoveryBlocked`，由用户“重新检查”或打开文件位置；
- 清理失败后按 30 秒、2 分钟、10 分钟、1 小时有限退避；达到上限后提供“立即重试”，且不阻塞其他文件；
- 可扩展的前置处理队列。当前内置处理器会按用户指令合并同名 MP4/M4A，并在输出通过 FFprobe 视频流/音频流验证后允许预览和正式入库；
- 原生独立窗口、单实例，以及软件关闭时对自有 FFmpeg 进程的受控终止。

处理成功不等于正式入库：原 MP4/M4A 会保留到用户再次确认“正式入库”且冲突决策、正式文件提交和数据库提交全部验证成功。成功后原 MP4、原 M4A 和处理工作输出才会清理。失败、中断、关闭冲突弹窗或源/目标变化时不删除原文件。

## 源码验证

NuGet 依赖位于 `apps\media-desktop\.packages`，可在不联网的情况下恢复和测试：

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

真实媒体集成测试使用仓库内固定的 `paired.mp4`、`paired.m4a`，以及受控位置 `C:\Program Files\ffmpeg\bin` 下的工具。普通源码构建不携带 FFmpeg 时仍可启动，前置处理显示稳定的工具缺失诊断，普通图片和视频流程不受影响。

调试启动：

```powershell
dotnet run --project apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64 --no-restore
```

## FFmpeg 来源验证

仓库只保存工具清单和第三方声明，不保存约 300 MiB 的可执行文件。固定清单位于：

```text
apps\media-desktop\tools\ffmpeg\win-x64\manifest.json
apps\media-desktop\tools\ffmpeg\win-x64\THIRD-PARTY-NOTICES.txt
```

发布脚本要求显式传入完整的 Gyan 分发根目录，并在发布前核对 `ffmpeg.exe`、`ffprobe.exe` 的大小和 SHA-256。当前固定版本为 `2025-06-23-git-e6298e0759-full_build-www.gyan.dev`，许可证为 GPL v3，来源提交为 `e6298e0759`。任何不匹配都会在替换现有测试包前停止。

## 离线测试包

发布：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 4B -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
```

生成：

```text
dist\TraceFold-MediaDesktop-Stage4B-win-x64
dist\TraceFold-MediaDesktop-Stage4B-win-x64.zip
```

包内工具只能位于：

```text
tools\ffmpeg\ffmpeg.exe
tools\ffmpeg\ffprobe.exe
tools\ffmpeg\LICENSE
tools\ffmpeg\README.txt
tools\ffmpeg\THIRD-PARTY-NOTICES.txt
```

离线烟雾测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage4B-win-x64' -StageCheckpoint 4B
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage4B-win-x64.zip'
```

烟雾测试将子进程 `PATH` 限制为 Windows `System32`，验证原生窗口、单实例、无浏览器启动、包内工具路径，以及五个 Stage 4A 冲突/媒体流程。Stage 4B 还会让独立探针进程分别在替换备份落盘后、正式文件提交后真实终止，再以正常无故障注入进程验证自动恢复、资产身份、目标哈希及安全清理。测试会检查提交/恢复目录为空、干净退出及无残留 FFmpeg；它不会下载或调用系统 `PATH` 中的 FFmpeg。

冲突规则按 [Stage 4A 手工验收指南](../../docs/media-desktop-stage4a-test-guide.md) 执行；恢复与清理按 [Stage 4B 手工验收指南](../../docs/media-desktop-stage4b-test-guide.md) 执行。不要把 Web 版、旧测试版或个人资料正在使用的目录用于测试。
