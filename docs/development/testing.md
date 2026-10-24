# 测试与验证

本页记录当前验证入口。命令从仓库根执行，按任务范围选取；实际运行需要使用 [环境与临时目录约定](../ENV_CONVENTIONS.md) 中的外层包装。运行期间保持受测源码不变。

## 选择验证层

| 层次 | 入口 | 结论范围 |
|---|---|---|
| .NET 回归 | `apps/media-desktop/TraceFold.MediaDesktop.slnx` | 对应项目的实际测试及 TRX |
| PowerShell 工具链 | `scripts/test-stage5cc-ui-acceptance-toolkit.ps1` | 受控支持程序、SDK、driver 或场景替身下的调用链 |
| 工具包结构 | `scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1` | 交付成员、固定工具、边界及 entry 自校验；默认不启动产品 |
| 产品身份 | `scripts/smoke-media-desktop-package.ps1 -IdentityOnly` | 只读身份检查，不是完整产品 smoke |
| 真实产品与 UI | `scripts/run-stage5cc-ui-acceptance.ps1` | 实际执行的产品、driver、桌面及场景；须单独限定操作范围 |

替身结果、真实 CLI 接缝结果与真实产品/UI 结果分开记录。已发布产品的验证范围和限制见 [发布验收摘要](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/ACCEPTANCE-SUMMARY.zh-CN.md)，不代表当前源码已重新验证。

## .NET 回归

SDK 由根 [global.json](../../global.json) 固定；源码准备和依赖恢复见 [开发起步](getting-started.md)。已有完整恢复和 Release 构建时，在外层临时环境的 `try` 内执行：

~~~powershell
$trxRoot = Join-Path $repoRoot ('artifacts/reports/dotnet/' + [guid]::NewGuid().ToString('N'))
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers --logger trx --results-directory $trxRoot
$testExit = $LASTEXITCODE
if ($testExit -ne 0) { throw "dotnet test exited $testExit" }
~~~

每次使用新的 TRX 叶。未构建或未恢复时，先按开发起步完成对应步骤；不以添加报告为由更换 SDK 或测试框架。

## PowerShell 工具链自测

runner 只有 `-Suite` 参数，默认 `All`，不接收 `-OutputDirectory`。允许值为：

~~~text
All Contracts Driver Processes Evidence Runner Workflows
IntakeScenario WorkflowScenario Aggregate HostCondition Metadata
OfflineEntry Packaging Launcher Visual
~~~

例如，在 [环境规范](../ENV_CONVENTIONS.md) 的外层 `try` 内执行一个 suite，控制台输出和退出码由调用方保存：

~~~powershell
$reportRoot = Join-Path $repoRoot ('artifacts/reports/toolkit-tests/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($reportRoot)
& $nativePs7 -NoProfile -File (Join-Path $repoRoot 'scripts/test-stage5cc-ui-acceptance-toolkit.ps1') -Suite OfflineEntry 2>&1 |
    Tee-Object -FilePath (Join-Path $reportRoot 'console.log')
$suiteExit = $LASTEXITCODE
$suiteExit | Set-Content -LiteralPath (Join-Path $reportRoot 'exit-code.txt')
if ($suiteExit -ne 0) { throw "OfflineEntry exited $suiteExit" }
~~~

同一受影响 suite 还需在原生 Windows PowerShell 5.1 下运行。另开一次相同的外层环境包装，获得新的 owned 临时叶，再在其 `try` 内使用新的报告叶：

~~~powershell
$nativePs51 = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$reportRoot51 = Join-Path $repoRoot ('artifacts/reports/toolkit-tests/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($reportRoot51)
& $nativePs51 -NoProfile -File (Join-Path $repoRoot 'scripts/test-stage5cc-ui-acceptance-toolkit.ps1') -Suite OfflineEntry 2>&1 |
    Tee-Object -FilePath (Join-Path $reportRoot51 'console.log')
$ps51Exit = $LASTEXITCODE
$ps51Exit | Set-Content -LiteralPath (Join-Path $reportRoot51 'exit-code.txt')
if ($ps51Exit -ne 0) { throw "PowerShell 5.1 suite exited $ps51Exit" }
~~~

两宿主分别使用新的持久报告叶和 owned 临时叶，按顺序运行；PowerShell 工具链与 dotnet 也串行。上述命令是调用示例，不会由文档自动执行。

`Packaging` 使用 `.cache/downloads/winappcli-x64.zip` 作为本地固定输入；按 [driver manifest](../../apps/media-desktop/tools/winapp/win-x64/manifest.json) 准备并核对，测试不会为缺失输入自动下载。该 suite 用受控 SDK/产品替身执行真实 publisher 与独立 smoke；它的 TRX 替身输出不等于 .NET 实测结果。`Visual` 自测也不等于真实视觉矩阵。

## 源码 UI entry 与工具包模式

入口参数、场景及矩阵能力以 [entry 源码](../../scripts/run-stage5cc-ui-acceptance.ps1) 和注册表为准。两种模式必须二选一：

| 模式 | 选择参数 | 省略 `-OutputDirectory` 时的输出 |
|---|---|---|
| 源码 | `-WinAppCliArchivePath` | `artifacts/evidence/stage5cc-uia-gate/run-<GUID>` |
| 便携工具包 | `-ToolkitDirectory` | 该工具包 `toolkit.json` 的 `resultsRoot` 下新运行叶 |

场景运行的两种模式都要求 `-ProductDirectory`。源码模式需要 SDK、本地已验 driver ZIP，并会准备支持程序；便携模式验证随包成员，不回到源码编译或自动下载。

`-OutputDirectory` 是这个 entry 的参数，显式目标必须是不存在的新叶。运行输出包含 `summary.json` 及实际到达场景的报告；未到达的检查不补成通过。

entry 的 `-ValidateOnly` 只校验工具包，须选择 `-ToolkitDirectory`；在启动产品、driver 或支持程序前退出。当前参数块把 `ProductDirectory` 标为 `Mandatory`，所以这个早退出模式也必须提供该参数，不能只传 `ToolkitDirectory`。独立 toolkit smoke 在内部传入两目录并使用此开关，smoke 本身没有名为 `-ValidateOnly` 的参数。

真实 UI 场景及 `-ThemeMatrix Both` 涉及产品/driver/交互桌面，矩阵还涉及主题操作；运行前确认目标产品、隔离数据目录和桌面操作范围。操作说明见 [Stage 5C-C 指南](../media-desktop-stage5cc-visual-uia-test-guide.md)，其中验收结果以对应实际记录为准。

## 运行记录

在新证据叶保存真实外层 argv、cwd、输入来源、目标/包装/工具退出码，以及运行前后的受影响源码 raw SHA。Git blob 与磁盘 CRLF/LF 分开绑定；source binding 不匹配时不可把旧结果声称为当前输入的全量验证。

先核“真实外层调用 → 场景 → 消费者 → 报告”，再补必要自测。保留首败和各运行原件；未知写 `Unknown`，未执行写 `NotRun`。打包、工具链回归和真实产品验收分别记录。

[返回开发导航](README.md) · [发布流程](releasing.md)
