# TraceFold Media Desktop

TraceFold Media Desktop 的 Stage 1 是原生 Windows 文件管理客户端的基础检查点。它提供 WinUI 3 独立窗口、三栏导航、目录设置、空的 SQLite 数据库、单实例激活和本地日志。

此阶段不会扫描、处理、移动、重命名或删除媒体文件，也尚未包含 FFmpeg。数据库只用于保存基础设置，正式的媒体入库能力将在后续阶段实现。

## 源码验证

在仓库根目录运行：

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx
dotnet run --project apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64
```

## 离线测试包

发布脚本会生成 `dist\TraceFold-MediaDesktop-Stage1-win-x64` 和对应 ZIP；烟雾测试会启动原生窗口、验证单实例和干净退出，并使用独立的临时数据目录。

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1
```
