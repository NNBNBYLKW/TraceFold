# TraceFold Media Desktop

TraceFold Media Desktop Stage 2 是原生 Windows 文件入库客户端的可验收检查点。它提供：

- 待入库目录手动刷新，以及默认关闭的 5 秒自动扫描；
- 图片和普通视频的稳定性判断、预览、元数据标注和正式入库；
- 多作者、单系列、多内容标签，以及显示名称与实际文件名分离；
- 正式文件库搜索、详情编辑和受控的“打开所在目录”；
- 同名、同内容冲突保护，以及可恢复的入库记录；
- 可扩展的前置处理分组。当前同名 MP4/M4A 会标记为“需要前置处理”，还不会调用 FFmpeg。

入库成功后，源文件从待入库目录移除，正式文件保留原始物理文件名。冲突、源文件变化或入库失败时，源文件保留。

## 源码验证

依赖包位于 `apps\media-desktop\.packages`，可在不联网的情况下运行：

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

调试启动：

```powershell
dotnet run --project apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64 --no-restore
```

## 离线测试包

发布脚本生成：

```text
dist\TraceFold-MediaDesktop-Stage2-win-x64
dist\TraceFold-MediaDesktop-Stage2-win-x64.zip
```

发布和烟雾测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1
```

烟雾测试会验证原生窗口、单实例、无浏览器启动、干净退出、非法数据目录失败，并在受控临时目录中完成一次 Stage 2 扫描—标注—入库流程。

手工验收请按 [Stage 2 手工验收指南](../../docs/media-desktop-stage2-test-guide.md) 执行。不要将 Web 版或个人资料的正在使用的目录用于测试。
