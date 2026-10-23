# 独立发布准备过程记录

本轮已按用户最新要求收尾当前产包小阶段并整合主线；最终范围、产物和停点见 [整合报告](RELEASE-PREP-INTEGRATION-20261023.md)。下文保留过程和首败记录，不将旧状态改写成成功。

2026-10-23，Europe/London。用户要求主代理与子代理在独立分支完成剩余问题，不采用六角色机制，不干扰原工作流。允许真实电脑操作；尚未授权 push、合并 main、标签或对外发布。

- 分支：`codex/release-prep-20261023`。
- 检出：`T:/Windows/Documents/GitHub/TraceFold/.superpowers/release-prep-20261023`。
- 基线：`1062ce881665c9c7f44f42f158b2e9f34d30f735`。
- 复制来源：原主目录当时的 28 件 tracked/untracked 候选文件；逐文件 SHA-256 已核对，记录在 `.superpowers/release-prep/source-snapshot.json`。未切换、暂存或改写原主目录源码。

## 当前证据

1. 原 `.packages` 直接作为新检出的 feed 还原失败，首败保留在 `.superpowers/release-prep/restore-20261023-131244/`。将其现有 nupkg 复制到本检出独立 flat feed，同名包校验 hash 后去重；无下载、无 SDK 升级。`restore-flat-20261023-131334/` 还原成功。
2. `build-20261023-131358/`：Release 构建成功，0 warning、0 error。
3. `dotnet-tests-20261023-131459/`：首次 .NET 回归 1247 项中的 1 项失败。原发布合同只计 3 次缓存参数，遗漏已新增的 Support 发布。断言按实际四条离线调用更新后，`dotnet-regression-20261023-131632-2423be/` 共 1247/1247 通过，6 份 TRX，运行前后源码 hash 未变。
4. `visual-chain-ps7-20261023-131852-05a841/`：受控整链首次失败，未 dispatch。测试支持程序路径 307 字符，文件存在而 CreateProcess 返回 Win32 error 2。PS7 将原始启动错误显示为 StandardOutputEncoding 错误；[PowerShell 原始问题记录](https://github.com/PowerShell/PowerShell/issues/28225)解释了错误遮蔽机制，长路径是本机最小诊断所得，不能仅由该外部记录推断。
5. `workflows-ps7-20261023-132449-71ca61/`、`workflows-ps51-20261023-132530-64be36/`：各 32/32 通过，目标 exit 0，受测源码运行前后不变。两类 timeout 都观察到真实退出与双流成功收口；继承管道反例记录 wrapperClosureObserved=false，测试清理另记，未冒充 wrapper 成功。
6. 61 身份/正常退出修复后，`workflow-scenarios-ps7-20261023-132858-6a13d4/`、`workflow-scenarios-ps51-20261023-132915-97ecc4/` 各 94/94 通过，目标 exit 0，受测源码不变。尚非真实 61 验收。
7. 新增 Support RID restore 后，缓存调用计数再从 4 更新为 5；`package-contract-20261023-132811-0f9d11/` 的 .NET 发布合同 9/9 通过，TRX 保留。
8. `packaging-ps7-20261023-132925-b68e07/`：23 项首轮失败，测试把 attempt 建在 TEMP 外，finally 的清理边界拒绝覆盖了更早结果；不得将此轮解释为产品故障或成功。23 个现场保留在本检出 `.superpowers/p/`，后续测试目录必须恢复为 TEMP 内 `tracefold-*`，不放宽清理规则。
9. `visual-chain-ps7-20261023-133317-124b1d/`：PS7 真实公开入口、70、Runner/Evidence 的四个受控组合全部符合预期；Source/Package 的 Both 各 exit 0、Passed、94 条，CurrentOnly 各 exit 1、NotRun、47 条。全部 Synthetic，journalAbsent=true，运行前后受测源码 hash 不变。实际运行树由每组 `setup.json.runtimeRoot` 指向 TEMP 内短路径；真实 ledger 与受控 Inventory/Stop 清理、包 p→m 整体搬移及源树 s→x 隔离均留证。独立复核未发现阻断项，尚未声明 PS5.1 或真实 UI 通过。
10. `packaging-ps7-20261023-133903-b4fd5c/`：23/23 通过，包含 SDK/TRX 失败、四目标 promotion 和 rollback、包污染与各历史 stage 的受控回归。
11. `toolkit-all-ps51-20261023-134408-5dd682/`：390 通过、5 失败。三个 Runner fixture 因深层 bootstrap 路径启动失败，缩短测试叶后 `runner-ps51-20261023-135225-b16387/`、`runner-ps7-20261023-135433-ab8f98/` 各 30/30 通过。受控进程状态读取改为 JSON 对象属性，避免 PS5.1 顶层数组管线形状差异；`visual-ps51-20261023-135643-c2c416/` 的四个整链组合全部符合预期，仍有一个启动门禁夹具失败（63/64）。
12. 上述门禁夹具的实际根因由 `currentonly-diagnostic-20261023-140322-4fcae1/evidence/child-observation.json` 确认：缺 driver manifest，公开入口在 gate 前的第 755 行退出。缩短路径未解决此失败，不能将它归为已证实的 MAX_PATH 问题。补复制固定 manifest，并保留失败 child 事实；`currentonly-fixed-ps51-20261023-140512-52587e/`、`currentonly-fixed-ps7-20261023-140517-720065/` 单例均 exit 0。中间 `currentonly-fixed-ps51-20261023-140431-f12018/` 为 scratch harness 未导入清理函数的失败，亦保留，不计通过。

13. 最终冻结源码完整回归：`toolkit-all-ps7-final-20261023-140627-6bce99/` 和 `toolkit-all-ps51-final-20261023-141814-ba31cf/` 均 395/395、targetExitCode=0、changedSource=[]。两个宿主各自的 Source/Package × CurrentOnly/Both 四个受控整链都符合预期，主题、进程 ledger 清理及整体搬移证据保留。

Task13 的开发残差已在上述受控范围闭合；61 身份/正常关闭与发布 RID/TRX 修复已完成开发回归及独立复核。下一步为绑定本分支提交的真实产包、产品 smoke 和真实 UI 验收，尚不宣告 Stage5CC 最终通过。

独立复核：Dewey 检查 61 身份/退出逻辑并核对两宿主 94/94 的源码 hash，无阻断项（既有 Runner 的产品进程清理不在该次复核范围）；Beauvoir 检查 publisher 的 RID/TRX/短 staging/四目标 promotion 与 rollback，无新增阻断项；Dewey 检查新的受控 integration 清理、搬移及已完成代表的留证，无阻断项。复核均不等于真实产品验收。

所有路径相对本检出的 `.superpowers/release-prep/`（首条 snapshot 除外）。历史失败保持原样；后续使用新输出叶。

## 当前写域与待办

- 主代理：整体验证、验收裁决、证据和本文件；受控整链验证与真实产品运行串行安排。
- 子代理：publisher/Packaging.Tests（Support RID 还原与 TRX）；61/WorkflowScenario.Tests（身份绑定、正常退出证据）；visual-integration fixture/VisualScenario.Tests（真实 ledger 清理、整体搬移、失败证据保留）。互不重叠。
- Workflows stdin 超时实现已经存在；本轮补强退出和双流实际完成断言，双宿主针对性回归已通过。
- startup 最后 readback 的 ExpectedAdapter 和独立 bootstrap 测试树已存在，待双宿主运行与验收。
- 仍需完成：Task13 开发闭包、PS7/PS5.1 完整回归、Task61 真实验收、独立产品/工具包产出及 smoke、真实 All/Both 主题布局键鼠矩阵、截图检查及所有 owned 进程/根清理核对。
- 未声明任何真实视觉或最终发布通过；clean VM 为 NotRun。

已核验离线发布输入：`C:/Program Files/ffmpeg`；`T:/Windows/Downloads/winappcli-x64.zip`（固定 v0.5.0，37881954 字节，SHA-256 `88735CE6C2582AC5FAC6200194BF62467FDD72B44B2D230F3A4ED059FA79EE7D`）。产包必须绑定本分支已提交源码，不复用旧产品作为本次验收结果。
