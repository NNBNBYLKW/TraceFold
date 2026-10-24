# TraceFold Media Desktop

TraceFold 的原生 Windows 媒体整理客户端，当前公开预发布版本为 **v4.0.0-stage5cc**。普通用户请从 [项目首页](../../README.md) 下载产品 ZIP，并按 [使用指南](../../docs/getting-started.md) 操作；无需为使用发布包构建源码。

当前功能包括待入库扫描与预览、同名 MP4/M4A 前置处理、资料标注与正式入库、冲突比较和中断恢复、正式文件库搜索筛选及词表关联。正式入库成功后会按策略清理源文件；首次使用请先准备素材副本。资料编辑自动保存，显示名称与物理文件名分开。

## 开发环境

- Windows 11 24H2（build 26100）或更新版本，x64。
- 仓库根 global.json 固定 .NET SDK **10.0.300**，允许同功能带的更新补丁（latestPatch）。
- 桌面项目使用 WinUI / Windows App SDK，目标框架为 net10.0-windows10.0.26100.0，平台与运行标识为 x64 / win-x64。开发机需要相应 Windows 构建环境。
- NuGet 依赖由 Directory.Packages.props 管理，恢复目录由 Directory.Build.props 指定为 apps/media-desktop/.packages。

**新克隆不自带本机的 .packages 缓存。** 首次恢复需要访问包源，或自行准备完整、可信的离线包源；不要把开发现场已有缓存视为仓库内容。以下命令在仓库根执行：

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source https://api.nuget.org/v3/index.json --disable-build-servers
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

若使用自己准备的离线源，将 restore 的 --source 替换为该源的路径；只有缓存完整并与依赖版本匹配时，才可离线恢复。仓库保留现有测试执行方式，不因文档示例要求升级 SDK 或测试框架。

调试启动：

~~~powershell
dotnet run --project apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64 --no-restore
~~~

以上是开发操作说明，不是本轮文档更新新执行的验证结果。不要用个人媒体库或正在使用的工作目录进行自动化测试。

## 媒体工具与发布包

产品包包含 .NET、Windows App SDK 运行组件及 FFmpeg / FFprobe，通常无需另装。源码仓库只保留 FFmpeg 清单与第三方声明，不包含大型工具二进制。普通源码构建不携带 FFmpeg 时，前置处理会显示工具缺失诊断；普通图片和视频流程与此分开。

工具清单与声明：

- tools/ffmpeg/win-x64/manifest.json
- tools/ffmpeg/win-x64/THIRD-PARTY-NOTICES.txt

发布脚本 scripts/publish-media-desktop.ps1（仓库根）要求显式传入 FFmpeg 分发根目录，并检查工具大小和 SHA-256。当前清单固定 Gyan 分发版本 2025-06-23-git-e6298e0759-full_build-www.gyan.dev，来源提交 e6298e0759；具体第三方授权以声明文件及随包文件为准，不据此推定 TraceFold 项目许可证。

Stage 5C-C 的两类交付物分别是产品包 TraceFold-MediaDesktop-Stage5C-C-win-x64.zip 与 UI 验收工具包 TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip。工具包供验证使用，不能代替产品包。已有公开版本无需为阅读本说明重新打包。

## 文档与验证边界

- [普通用户使用指南](../../docs/getting-started.md)
- [常见问题与排障](../../docs/troubleshooting.md)
- [支持与反馈](../../SUPPORT.md)
- [Stage 5C-C 视觉与 UI 自动化指南](../../docs/media-desktop-stage5cc-visual-uia-test-guide.md)
- [Stage 4A 冲突流程参考](../../docs/media-desktop-stage4a-test-guide.md)
- [Stage 4B 恢复与清理参考](../../docs/media-desktop-stage4b-test-guide.md)

旧阶段指南用于对应功能的历史参考，当前下载与普通使用入口以项目首页和使用指南为准。已验证范围及限制统一见 [版本状态](../../README.md#版本状态)。
