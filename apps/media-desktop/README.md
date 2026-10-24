# TraceFold Media Desktop

TraceFold 的原生 Windows 桌面媒体整理客户端，提供待入库扫描、照片与视频预览、同名 MP4/M4A 前置处理、正式入库、资料编辑、搜索筛选和词表管理。

普通用户使用 [产品下载与快速开始](../../README.md) 和 [使用指南](../../docs/getting-started.md)。开发者从 [开发文档](../../docs/development/README.md) 进入；[源码起步](../../docs/development/getting-started.md) 提供恢复依赖、构建与调试启动命令。

## 项目配置

| 项目 | 当前值 |
| --- | --- |
| 开发平台 | Windows 11 24H2（build 26100）或更新版本，x64 |
| SDK | 根 global.json 指定 .NET SDK 10.0.300，rollForward=latestPatch |
| UI 与目标框架 | WinUI / Windows App SDK；net10.0-windows10.0.26100.0 |
| 平台 / 运行标识 | x64 / win-x64 |
| 解决方案 | TraceFold.MediaDesktop.slnx |
| 桌面启动项目 | src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj |
| 程序文件名 | TraceFold.Media.exe |
| 依赖管理 | Directory.Packages.props；Directory.Build.props 指定项目内 .packages |

新克隆不包含 .packages 缓存、bin/obj、FFmpeg 或验收 driver 二进制，需要按对应开发或验证步骤准备。发布产品包包含 .NET、Windows App SDK 运行组件与 FFmpeg / FFprobe，普通使用通常无需另装；验证范围见 [版本状态](../../README.md#版本状态)。

## 组件与工具

- src/：Domain、Application、Infrastructure 与 Desktop。
- tests/：对应组件、架构及支持工具的测试。
- tools/TraceFold.Media.UiAcceptance.Support/：验收支持 CLI。
- [FFmpeg 固定清单](tools/ffmpeg/win-x64/manifest.json) 与 [第三方声明](tools/ffmpeg/win-x64/THIRD-PARTY-NOTICES.txt)。
- [winapp CLI 固定清单](tools/winapp/win-x64/manifest.json) 与 [来源说明](tools/winapp/win-x64/SOURCE-NOTICE.txt)。

开发构建使用 TraceFoldFfmpegSourceRoot 属性提供含 bin/ffmpeg.exe、bin/ffprobe.exe 的工具分发根目录；未提供工具时，前置处理会显示工具缺失诊断。发布脚本按清单核验工具大小与 SHA-256。组件声明不等于 TraceFold 项目的许可证。

## 开发参考

- [源码起步](../../docs/development/getting-started.md)
- [测试与证据](../../docs/development/testing.md)
- [构建发布](../../docs/development/releasing.md)
- [工作区布局](../../docs/development/workspace-layout.md)
- [Stage 5C-C 视觉/UIA 指南](../../docs/media-desktop-stage5cc-visual-uia-test-guide.md)
- [Stage 5C-C 验收记录](../../docs/media-desktop-stage5cc-visual-uia-acceptance.md)
