# TraceFold Media Desktop Stage 4 验收记录

## 验收对象

- 验收时间：2026-08-24 10:18:46 +08:00；
- 验收分支：`codex/native-media-desktop-stage4`；
- 对比基线（与 `main` 的 merge-base）：`998aa7588769cfc8468791495a40e5596e2df2e5`；
- Stage 4A 功能与测试包源提交：`e63c5d202ca61a5b5f0f4e0c9a429075c8824433`；
- Stage 4B 功能与最终测试包源提交：`65c196958c60e6bab2bc2db6dbd0c1930e8a88eb`；
- 平台：Windows 11 x64，本机离线依赖缓存，无网络下载。

Stage 4A 和 Stage 4B 的人工验收均由用户在 2026-08-24 报告通过。Stage 4B 通过后，Stage 4 作为一个整体进入已验收状态。

## Release 构建与全量测试

Stage 4B 最终功能提交执行离线恢复、Release 构建和全量测试。隔离工作树中的 `.packages` 是重解析入口，因此恢复命令明确使用主工作区中的真实本地缓存路径：

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx `
  --source T:\Windows\Documents\GitHub\TraceFold\apps\media-desktop\.packages `
  --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx `
  -c Release --no-restore
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx `
  -c Release --no-build --no-restore
```

结果：恢复成功，Release 构建 **0 警告、0 错误**；全量测试 **473/473** 通过、0 失败、0 跳过。

| 测试项目 | 通过 |
|---|---:|
| Domain | 41 |
| Application | 164 |
| Architecture | 5 |
| Infrastructure | 180 |
| Desktop | 83 |
| 合计 | 473 |

为确认 Stage 4A 验收点本身可独立复现，另在临时 detached worktree 中重放提交 `e63c5d2`：离线恢复成功，Release 构建 0 警告、0 错误，**398/398** 项测试通过。分项为 Domain 41、Application 122、Architecture 5、Infrastructure 156、Desktop 74。证据采集完成后已移除该临时 worktree。

## Stage 4 安全专项测试

按冲突领域状态、恢复与清理应用服务、真实 SQLite/文件系统恢复，以及 Desktop 冲突与探针入口执行重点过滤测试：

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ConflictCaseTests|FullyQualifiedName~IngestOperationTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Conflict|FullyQualifiedName~IngestRecovery|FullyQualifiedName~IngestCleanup|FullyQualifiedName~IngestFault"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Stage4MigrationTests|FullyQualifiedName~ConflictResolutionFlowTests|FullyQualifiedName~IngestCrashRecoveryFlowTests|FullyQualifiedName~IngestCleanupWorkerTests|FullyQualifiedName~ControlledMediaStorageTests|FullyQualifiedName~Stage4LoggingPrivacyTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ConflictDialog|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~Stage4PackageProbeOptionsTests"
```

结果：**189/189** 通过、0 失败、0 跳过。

| 测试项目 | 通过 |
|---|---:|
| Domain | 12 |
| Application | 78 |
| Infrastructure | 65 |
| Desktop | 34 |
| 合计 | 189 |

其中两组发布门槛又单独执行并通过：

- `Stage4MigrationTests`：**3/3**。验证 Stage 3 数据图原样升级、重复内容哈希允许共存、正式路径仍唯一，以及冲突、备份和延迟清理字段完整；
- `IngestCrashRecoveryFlowTests`：**12/12**。其中 9 个案例逐一覆盖 `AfterOperationCreated`、`AfterStaged`、`AfterBackupCreated`、`AfterFileCommitted`、`BeforeDatabaseCommit`、`AfterDatabaseCommit`、`BeforeCleanup`、`AfterCleanupEntry`、`AfterCleanup`；另外 3 个案例验证目标、备份或数据库版本矛盾时进入 `RecoveryBlocked` 并保留所有可观察文件。

## 离线包与烟雾测试

最终产物位于当前 Stage 4 工作树：

```text
dist\TraceFold-MediaDesktop-Stage4A-win-x64
dist\TraceFold-MediaDesktop-Stage4A-win-x64.zip
dist\TraceFold-MediaDesktop-Stage4B-win-x64
dist\TraceFold-MediaDesktop-Stage4B-win-x64.zip
```

| 验收点 | 源提交 | 目录文件数 / 字节 | ZIP 字节 | ZIP SHA-256 |
|---|---|---:|---:|---|
| Stage 4A | `e63c5d202ca61a5b5f0f4e0c9a429075c8824433` | 577 / 551,191,485 | 219,649,264 | `9C9F2D51B1374F7BC8D2210BB446CAD0EB2173D2F3FC4C17DC5BBBF17E5D5301` |
| Stage 4B | `65c196958c60e6bab2bc2db6dbd0c1930e8a88eb` | 577 / 551,282,974 | 219,680,710 | `7F1A6FD3C890766520CE2728476A43C3B693306CC7868A6E4BE3A6AECCE1720B` |

Stage 4B 最终重新发布后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts\smoke-media-desktop-package.ps1 `
  -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage4B-win-x64' `
  -StageCheckpoint 4B
```

结果：

- 原生独立窗口、单实例、受限 `PATH` 离线启动、无浏览器启动和干净退出通过；
- Stage 4A 的名称、内容、组合、未登记目标、跳过、保留两者、替换及外部变化冲突场景通过；
- 随包 FFmpeg 的 MP4/M4A 前置处理、预览、冲突与正式入库通过；
- Stage 4B 在替换备份落盘后和正式文件提交后两个真实进程强制终止检查点均自动恢复；
- 恢复后资产 ID、目标哈希、资产唯一性、源文件、备份和未知正式文件检查通过；
- 烟雾测试结束后没有包内 TraceFold 或 FFmpeg 残留进程。

用户人工验收的是重新发布前、同一源提交生成的 Stage 4B 包，当时记录的 ZIP SHA-256 为 `AE8FA714181ED4642A445DB533A564C0BFCAFD2E84440BD92634EB637D717639`。最终重新发布改变了 ZIP 归档元数据，因此 ZIP 字节哈希更新为上表值；功能源提交不变，最终目录重新生成并重新通过完整包级烟雾测试。

人工验收清单：

- [Stage 4A 人工验收指南](media-desktop-stage4a-test-guide.md)：用户报告通过；
- [Stage 4B 人工验收指南](media-desktop-stage4b-test-guide.md)：用户报告通过。

## 固定媒体工具

- 分发版本：`2025-06-23-git-e6298e0759-full_build-www.gyan.dev`；
- `ffmpeg.exe` SHA-256：`D5A3CDFF713F3DE9C3EC6CD0DD1BCD046BB0EB97712BD32D340E39D1F88DEF05`；
- `ffprobe.exe` SHA-256：`E6C17A4AE25E03275ADB00F664448D5D37ADDB88AE82372BAF1DC8BBDC7C3BB2`。

包内 `tools\ffmpeg` 只有 `ffmpeg.exe`、`ffprobe.exe`、`LICENSE`、`README.txt` 和 `THIRD-PARTY-NOTICES.txt`。生产运行只使用包内绝对工具路径，不搜索系统 `PATH`，不包含联网下载代码。

## 代码与包卫生

- `git diff --check main...HEAD` 通过；
- 验收记录写入前分支工作区干净；
- Git 跟踪的 `dist`、运行日志、用户数据库、崩溃转储和 FFmpeg/FFprobe 可执行文件计数均为 0；
- Stage 4B 包内 PDB、C# 源码/项目文件、数据库、日志、工作目录、探针结果、崩溃转储、Python、Node 和浏览器文件计数均为 0；
- 包内 FFmpeg/FFprobe 只存在于 `tools\ffmpeg`；
- 最终复核时没有 TraceFold 或 FFmpeg 残留进程。

## 规格不变量复核

| 不变量 | 复核结果 |
|---|---|
| 单一入库管线 | 无冲突、保留两者和替换均回到同一个 `IngestOperation` 状态机，没有建立第二套冲突入库流程。 |
| Desktop 边界 | Desktop 只消费 DTO 和应用层动作；路径受控、哈希比较、重名后缀、替换和清理规则留在 Application/Infrastructure。5/5 架构测试通过。 |
| 冲突持久化 | 名称、内容、组合及多匹配项写入持久化案例；关闭或 `Esc` 不提交决定，重启可重新打开。 |
| 单目标替换 | 只有内容冲突时必须明确选中目标；替换只影响一个匹配项，不批量替换相同哈希资产。 |
| 决策重新验证 | 输入、处理输出、目标文件或资产版本变化会使旧案例失效，不静默降级为新建或继续覆盖。 |
| 资产身份与资料 | 替换已登记资产保留资产 ID 和正式实际文件名，同时替换显示名称、作者、单一系列和多内容标签快照。 |
| 文件不丢失 | 任何正式文件变更前均先持久化恢复证据；旧目标、已验证备份、新目标和数据库按设计顺序交接有效副本。 |
| 恢复保守性 | 启动恢复只依据操作 ID、受控路径、大小、哈希、数据库版本和持久化快照自动推进；证据矛盾时停在 `RecoveryBlocked`。 |
| 恢复幂等 | 九个持久化故障点重启后不重复创建资产、不重复应用资料关系，不留下未知正式文件或无法归属的备份。 |
| 故障入口隔离 | 正常依赖注入固定注册 `NoOpIngestFaultInjector`；诊断注入只由严格令牌、系统临时根、固定样本哈希和白名单参数约束的包探针显式构建，无生产 UI 或环境变量入口。 |
| 延迟清理 | 清理只处理持久化受控清单；单项成功立即落库，失败不回滚已提交资产。退避为 30 秒、2 分钟、10 分钟、1 小时，之后等待启动或手动重试。 |
| 项目隔离 | 一个项目的 `RecoveryBlocked` 或 `CleanupPending` 不阻塞其他项目浏览、处理和入库。 |
| 迁移兼容 | Stage 3 资产、资料关系、前置处理和清理记录升级后保持；内容哈希改为非唯一，正式路径仍唯一。 |
| 隐私与日志 | Stage 4 日志专项测试验证不记录完整路径、实际文件名、显示名称、作者、系列、标签、媒体 SHA-256 或探针令牌。 |

## 已知限制与最终结论

本轮没有可用的干净 Windows 11 x64 虚拟机或另一台全新机器。受限 `PATH` 的本机离线包烟雾测试和用户本机人工验收均已通过，但不能替代“全新 Windows 11 x64 系统、没有 SDK 或开发工具”验证。因此该项继续保留为未来正式发布门槛。

除上述干净系统发布门槛外，Stage 4A 与 Stage 4B 的设计验收项、自动化测试、数据库迁移、离线包烟雾测试和本机人工验收均通过，**TraceFold Media Desktop Stage 4 验收结论：通过**。
