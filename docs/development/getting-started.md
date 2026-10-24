# 桌面源码起步

本文用于从仓库源码构建与调试 TraceFold Media Desktop。普通用户请使用 [产品 ZIP 与使用指南](../../README.md)。

[开发文档](README.md) · [布局参考](workspace-layout.md) · [测试](testing.md) · [发布](releasing.md)

## 环境与工作目录

- Windows 11 24H2（build 26100）或更新版本，x64。
- 根 global.json 指定 .NET SDK 10.0.300，rollForward=latestPatch，allowPrerelease=false。使用相同功能带的受支持补丁版本。
- 桌面目标为 net10.0-windows10.0.26100.0，使用 WinUI / Windows App SDK；开发机需要对应 Windows 构建环境。
- 下列命令均从包含 global.json、apps、scripts 的仓库根执行。PowerShell 工具脚本使用原生 PS7 或 Windows PowerShell 5.1；各工具链按任务要求串行。
- 执行工具前，按 [环境约定](../ENV_CONVENTIONS.md) 在实际最外层设置本工作区 .cache/tmp 下的 TEMP/TMP，并在 finally 恢复；下方示例中的 dotnet 命令在这一运行环境内执行。

先核对实际检出状态，保留已有修改：

~~~powershell
git branch --show-current
git rev-parse HEAD
git status --short
~~~

## 恢复依赖

新克隆不自带项目 .packages。需要可访问的包源，或自行准备完整可信的离线包源。依赖版本由 apps/media-desktop/Directory.Packages.props 管理，恢复目录由同目录 Directory.Build.props 指定。

从公共 NuGet 源恢复：

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source https://api.nuget.org/v3/index.json --disable-build-servers
~~~

使用自备离线包源时，将 --source 后的地址替换为该源路径。离线源须完整匹配所需依赖和运行时包；不要把另一位维护者本机存在的缓存当作仓库已经携带。

## 构建

~~~powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

解决方案包含桌面、领域、应用、基础设施、支持 CLI 与测试项目。构建输出位于相应项目的 bin/obj；目录结构见 [布局参考](workspace-layout.md)。运行测试与保存 TRX 按 [测试文档](testing.md) 执行。

## 调试启动

~~~powershell
dotnet run --project apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64 --no-restore
~~~

该命令构建 Debug 输出并启动桌面程序。运行时请使用专门的测试素材副本与三个互不包含的目录，先核对应用设置再操作；入库成功后的源清理语义见 [使用指南](../getting-started.md#源文件会怎样处理)。应用资料与正式媒体文件不是构建缓存。

## 前置处理与第三方输入

发布产品包内携带运行组件与媒体工具；源码构建所需输入按下表准备：

| 输入 | 当前约定 |
| --- | --- |
| NuGet 依赖 | apps/media-desktop/.packages，由 restore 准备 |
| FFmpeg / FFprobe | 不随源码携带；固定清单见 [manifest.json](../../apps/media-desktop/tools/ffmpeg/win-x64/manifest.json)，第三方声明见 [说明](../../apps/media-desktop/tools/ffmpeg/win-x64/THIRD-PARTY-NOTICES.txt) |
| winapp CLI v0.5.0 x64 | 用于验收而非普通启动；[固定清单](../../apps/media-desktop/tools/winapp/win-x64/manifest.json) 规定输入大小与 SHA-256；Packaging fixture 的 ZIP 放 .cache/downloads/winappcli-x64.zip |

需要在源码调试中使用媒体前置处理时，通过 MSBuild 属性 TraceFoldFfmpegSourceRoot 指定 FFmpeg 分发根目录，内部应有 bin/ffmpeg.exe 和 bin/ffprobe.exe。发布流程使用 -FfmpegSourceRoot 参数，并验证固定清单；完整输入和命令见 [发布文档](releasing.md)。不提供工具时，前置处理显示工具缺失诊断。

## 下一步

- 选择直接受影响的 [测试](testing.md)，区分受控 fixture 与真实产品/UI 证据。
- 查 [当前布局](workspace-layout.md) 设置本次临时目录及持久结果位置。
- 需要生成交付物时使用 [发布文档](releasing.md)；普通构建和调试不等于发布。

artifacts 下的维护者本地回执和 .worktrees 下的冻结上下文均为可选资料，fresh clone 不需要从这些目录恢复才能开始开发。
