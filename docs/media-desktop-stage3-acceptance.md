# TraceFold Media Desktop Stage 3 验收记录

## 验收对象

- 验收时间：2026-08-23 12:07:26 +08:00
- 分支：`codex/native-media-desktop-stage3`
- 代码与测试包提交：`8459f40624b37400ac7dc888661d97c4bf5d7806`
- 对比基线 `main`：`be363084c02ffb8dda88e2f32febf17fb237c52a`
- 平台：Windows 11 x64，本机离线依赖缓存，无网络下载

## Release 构建与全量测试

执行：

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

结果：恢复成功，Release 构建 **0 警告、0 错误**，共 **285/285** 个测试通过、0 失败、0 跳过。

| 测试项目 | 通过 |
|---|---:|
| Domain | 24 |
| Application | 77 |
| Architecture | 5 |
| Infrastructure | 114 |
| Desktop | 65 |
| 合计 | 285 |

## 重点集成与关闭测试

执行：

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Mp4M4a|FullyQualifiedName~ProcessedMediaIngestFlowTests|FullyQualifiedName~ProcessingQueueWorkerTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~AppShutdownCoordinatorTests|FullyQualifiedName~Stage3PackageProbeOptionsTests"
```

结果：

- 真实 MP4/M4A、处理后正式入库和串行队列：**6/6** 通过；
- 关闭协调器和 Stage 3 安全包探针：**20/20** 通过；
- 真实输出同时包含视频流、音频流和正时长；失败路径保留原文件；停止/关闭不会留下自有子进程；
- 探针拒绝错误令牌、嵌套根、额外条目、已存在运行目录及哈希变化的样本。

## 最终离线包

当前隔离工作区中的产物：

```text
dist\TraceFold-MediaDesktop-Stage3-win-x64
dist\TraceFold-MediaDesktop-Stage3-win-x64.zip
```

- 包目录：575 个文件，550,953,856 字节；
- ZIP：219,579,099 字节；
- ZIP SHA-256：`97787CA83F6288CAAA306F8C7A06E8B08A449BFA88E0E554C294D6F396F369D3`。

发布命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -FfmpegSourceRoot 'C:\Program Files\ffmpeg' -NugetPackageSource 'T:\Windows\Documents\GitHub\TraceFold\apps\media-desktop\.packages'
```

`-NugetPackageSource` 只用于隔离 worktree 读取主工作区的实体离线缓存；在主工作区可省略，默认仍使用 `apps\media-desktop\.packages`。发布脚本先验证工具与许可证，完整生成暂存包和 ZIP 后才原子替换现有 Stage 3 产物。

## 固定媒体工具

- 分发版本：`2025-06-23-git-e6298e0759-full_build-www.gyan.dev`；
- FFmpeg 首行：`ffmpeg version 2025-06-23-git-e6298e0759-full_build-www.gyan.dev Copyright (c) 2000-2025 the FFmpeg developers`；
- 上游提交：`https://github.com/FFmpeg/FFmpeg/commit/e6298e0759`；
- 许可证：GPL v3，包内包含原分发 `LICENSE`、`README.txt` 和仓库第三方声明。

| 文件 | 大小（字节） | SHA-256 |
|---|---:|---|
| `tools\ffmpeg\ffmpeg.exe` | 153,995,264 | `D5A3CDFF713F3DE9C3EC6CD0DD1BCD046BB0EB97712BD32D340E39D1F88DEF05` |
| `tools\ffmpeg\ffprobe.exe` | 153,799,680 | `E6C17A4AE25E03275ADB00F664448D5D37ADDB88AE82372BAF1DC8BBDC7C3BB2` |

包内 `tools\ffmpeg` 只有 `ffmpeg.exe`、`ffprobe.exe`、`LICENSE`、`README.txt` 和 `THIRD-PARTY-NOTICES.txt`。

## 受限 PATH 烟雾结果

执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage3-win-x64'
```

子进程 `PATH` 被限制为 `%SystemRoot%\System32`。结果：

- 原生窗口、单实例、无浏览器启动和干净退出通过；
- 探针报告的工具路径严格等于包内 `tools\ffmpeg\ffmpeg.exe`；
- 固定 `paired.mp4`、`paired.m4a` 完成扫描、手动排队、串行处理、视频/音频预览验证和正式入库；
- 正式库保留 `paired.mp4` 物理文件名，两个原文件和处理工作输出被清理；
- 结束后包内 `TraceFold.Media.exe` 残留数为 0，包内 `ffmpeg.exe` 残留数为 0。

## 代码与包卫生

- `git diff --check main...HEAD` 通过；
- Git 跟踪的 `dist` 文件数为 0；
- Git 跟踪的 FFmpeg/FFprobe 可执行文件数为 0，仅保存清单和第三方声明；
- 包内 PDB、C# 源码/项目文件、用户数据库、日志、工作目录、固定媒体样本、Python、Node 和浏览器文件计数均为 0；
- 包内 FFmpeg/FFprobe 只存在于 `tools\ffmpeg`。

## 规格不变量复核

1. 扫描、启动和资料路径没有处理排队调用；只有用户处理命令创建队列任务。
2. ViewModel 和统一入库服务没有 MP4/M4A 参数逻辑；FFmpeg 映射参数只位于内置处理器。
3. 依赖注入只注册一个 `ProcessingQueueWorker`；领取顺序为 `QueuedAtUtc` 后按任务 ID，且领取受串行门保护。
4. 取消只持有并终止当前任务创建的精确 `Process` 实例及其进程树。
5. 启动恢复将既有排队/运行任务置为已中断，清理未验证输出，不自动恢复。
6. 处理输出必须经 FFprobe 验证视频流、音频流和正时长。
7. 正式入库前重新核对两个原文件以及处理输出的大小、时间和 SHA-256；变化会使输出失效。
8. 只有正式目标提交、数据库提交并再次验证成功后才逐项删除原文件和处理输出。
9. 多文件清理逐项持久化、受控路径保护且可幂等恢复。
10. Stage 2 普通媒体、资料、词表、预览、冲突和正式库回归测试全部保持通过。
11. 生产工具定位只使用包内绝对路径，不搜索 `PATH`，不存在下载代码。

## 人工验收与已知限制

人工验收清单：[media-desktop-stage3-test-guide.md](media-desktop-stage3-test-guide.md)。

本轮没有可用的干净 Windows 11 x64 虚拟机或另一台全新机器。受限 `PATH` 的本机离线烟雾测试已通过，但它不能替代干净系统验证。因此“全新 Windows 11 x64 机器解压即用、无 SDK/开发工具依赖”仍保留为未来正式发布门槛，本记录不将其标记为通过。
