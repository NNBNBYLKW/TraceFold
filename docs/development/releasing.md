# 本地发布与交付检查

本页记录当前 Stage 5C-C 本地产包入口。发布会恢复依赖、运行 .NET 测试、构建产品及工具包、执行产品 smoke，并更新根 `dist` 中的交付物。仅在任务明确包含这些操作时执行；文档示例本身不运行命令。

当前已发布版本为 [v4.0.0-stage5cc 预发布版](https://github.com/NNBNBYLKW/TraceFold/releases/tag/v4.0.0-stage5cc)，验证范围和限制见 [发布验收摘要](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/ACCEPTANCE-SUMMARY.zh-CN.md)。本地产包与上传 GitHub Release 是不同操作；下列命令仅在本地生成和检查交付物。

## 输入

从仓库根准备以下内容：

| 输入 | 当前要求 |
|---|---|
| 源码 | 已提交的 tracked 源码；publisher 拒绝未提交的 tracked 改动 |
| SDK | 根 [global.json](../../global.json) 指定的 .NET SDK；原生 Windows 构建环境 |
| NuGet | 完整本地目录，默认 `apps/media-desktop/.packages`；新 clone 不自带缓存 |
| FFmpeg | 与 [FFmpeg manifest](../../apps/media-desktop/tools/ffmpeg/win-x64/manifest.json) 大小及 SHA-256 一致的分发根 |
| winapp CLI | 与 [driver manifest](../../apps/media-desktop/tools/winapp/win-x64/manifest.json) 一致的本地 ZIP |

FFmpeg 输入是分发根，须包含 `bin/ffmpeg.exe`、`bin/ffprobe.exe` 和脚本检查的随附声明文件。driver 固定为 winapp CLI `0.5.0` x64，文件名 `winappcli-x64.zip`，大小 `37,881,954` bytes，SHA-256：

~~~text
88735CE6C2582AC5FAC6200194BF62467FDD72B44B2D230F3A4ED059FA79EE7D
~~~

工作区下载输入放在 `.cache/downloads`；FFmpeg/driver 输入不由脚本自动下载，也不以同名文件代替固定身份。完整 FFmpeg pins 以 manifest 为准。第三方许可证不代表 TraceFold 项目授权。

## 发布命令

先按 [环境规范](../ENV_CONVENTIONS.md) 建立原生 PowerShell 与 owned `TEMP/TMP` 外层环境。下面代码放在其 `try` 内，且须先把匹配 manifest 的 FFmpeg 分发根准备在示例位置：

~~~powershell
$ffmpegRoot = (Resolve-Path -LiteralPath '.cache/tools/ffmpeg').Path
$driverArchive = (Resolve-Path -LiteralPath '.cache/downloads/winappcli-x64.zip').Path
& $nativePs7 -NoProfile -File (Join-Path $repoRoot 'scripts/publish-media-desktop.ps1') -StageCheckpoint '5C-C' -FfmpegSourceRoot $ffmpegRoot -WinAppCliArchivePath $driverArchive
$publishExit = $LASTEXITCODE
if ($publishExit -ne 0) { throw "Publisher exited $publishExit" }
~~~

`.cache/tools/ffmpeg` 是此示例为已准备输入选择的目录，不是仓库自带工具。也可以把 `$ffmpegRoot` 指向你准备的、符合 pins 的分发根绝对路径。

publisher 的当前参数：

| 参数 | 用途 |
|---|---|
| `-StageCheckpoint` | 必填；当前交付选择 `5C-C` |
| `-FfmpegSourceRoot` | 必填；FFmpeg 分发根绝对路径 |
| `-NugetPackageSource` | 可选；完整本地 NuGet 目录的绝对路径，默认 canonical `.packages` |
| `-WinAppCliArchivePath` | `5C-C` 必需；已验 driver ZIP 的绝对路径 |
| `-FaultPoint`、`-RecoveryFaultPoint` | 故障注入验证 hook，真实发布保留默认 `None` |
| `-ProductSmokeLauncher` | 受控测试替身 hook，真实发布不传入 |

`-NugetPackageSource` 是本地目录，不是网络 URL。source 起步的 NuGet 恢复命令见 [getting-started](getting-started.md)。

## 当前输出

| 内容 | 路径 |
|---|---|
| 产品展开包 P | `dist/TraceFold-MediaDesktop-Stage5C-C-win-x64` |
| 产品 ZIP | `dist/TraceFold-MediaDesktop-Stage5C-C-win-x64.zip` |
| UI 验收工具包 K | `dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64` |
| K ZIP | `dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip` |
| 发布测试 TRX | `artifacts/reports/publish-media-desktop/<GUID>` |

publisher 的临时 staging 与回滚备份位于 `dist` 的脚本自管新叶。两个展开包及两个 ZIP 通过检查后一起晋升；失败按原 Guard/回滚机制处理，不能人工删除尚有恢复价值的备份来“收尾”。TRX 位于 staging 之外，失败也保留。

产品包含 `TraceFold.Media.exe`、运行依赖和 `tools/ffmpeg`。K 包含验收 entry、支持程序、固定 driver、模块、模板、合同及声明 fixture；K 不是普通用户产品包。`artifacts`、`.cache`、`.worktrees` 等私有目录即使带 Hidden 属性也必须被消费者拒绝，不能放入产品目录。

## 独立检查

在同一外层临时环境内定义：

~~~powershell
$product = Join-Path $repoRoot 'dist/TraceFold-MediaDesktop-Stage5C-C-win-x64'
$toolkit = Join-Path $repoRoot 'dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
~~~

工具包结构检查：

~~~powershell
& $nativePs7 -NoProfile -File (Join-Path $repoRoot 'scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1') -ToolkitDirectory $toolkit -ProductDirectory $product
$toolkitExit = $LASTEXITCODE
if ($toolkitExit -ne 0) { throw "Toolkit smoke exited $toolkitExit" }
~~~

此脚本参数只有 `ToolkitDirectory`、`ProductDirectory`、`ExpectedVersion`、`RunStartupLayout`。默认在内部调用 entry 的 `-ValidateOnly`，不启动真实产品、支持程序或 driver；显式 `-RunStartupLayout` 才委托实际启动场景。结构通过不等于真实 UI 通过。

只读产品身份检查：

~~~powershell
& $nativePs7 -NoProfile -File (Join-Path $repoRoot 'scripts/smoke-media-desktop-package.ps1') -PackageDirectory $product -StageCheckpoint '5C-C' -IdentityOnly
$productIdentityExit = $LASTEXITCODE
if ($productIdentityExit -ne 0) { throw "Product identity check exited $productIdentityExit" }
~~~

`-IdentityOnly` 只读检查产品目录及身份，不启动产品、不修改产品或用户数据；它不验证 FFmpeg pins，也不替代完整 smoke。正常 publisher 的产品 gate 使用完整 smoke，真实发布会实际启动产品探针；不要把 publisher 当纯结构检查器。

构建身份记录在 `package-version.json`，K 的成员身份记录在 `toolkit.json`；核对两包源提交、版本与 ZIP SHA-256。现有已发布产品的源码身份不会因本地文档提交自动改变。保存新运行的证据与首败，结论按 [测试分层](testing.md) 表述。

[返回开发导航](README.md)
