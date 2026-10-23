# 当前恢复点

## 2026-10-23 发布准备收尾后的状态

以 [本轮整合报告](RELEASE-PREP-INTEGRATION-20261023.md) 为最新进度入口。下方旧恢复点是迁移时的历史记录，暂停状态及四项残差不代表本轮结束状态。

- 实现提交：`36ffcf8`、`7977c6d`、`8ce837a`；本轮成果按用户最新要求整合到本地 `main`，未 push、打标签或对外发布。
- Task13 开发残差已闭合：PS7/PS5.1 各 395/395，公开入口至真实70/Runner/Evidence 的 Source/移动Package × Both/CurrentOnly 受控整链通过，Synthetic 标记保留。
- 当前小阶段已收尾：修复 Stage5CC 运行清单兼容性，真实发布成功，.NET 1260/1260，产品真实 smoke 和工具包结构校验通过。两份 ZIP 绑定 `8ce837a6909ef4f0b6f6f62360fad1d0817175f3`。
- 停点：尚未运行正式真实 `SingleInstance` 场景或 `All -ThemeMatrix Both`，未完成真实视觉/键鼠矩阵、截图终审和最终 Stage5CC 验收；clean VM 为 NotRun。
- 整合操作仅在 `T:/Windows/Documents/GitHub/TraceFold/.superpowers/release-prep-20261023` 完成。原主目录仍为 `codex/main-workspace-20261023`、HEAD `1062ce8`，既有28件候选文件哈希未变。不要直接覆盖它或 reset/stash 来切换主线。

## 迁移时恢复点（历史原文保留）

2026-10-23（Europe/London）。本页是事实摘要，不是最终产品验收记录。迁移期间没有重跑项目测试。

当前已确定六成员工作流：总负责人、助理、协调员、执行者、reviewer、攻坚者，具体职责及外部检索、问题升级和暂停规则见根AGENTS.md。六名成员均已初始化；本轮仅授权工作流文档小任务；Task13及真实产品/UI开发仍暂停；下述代码及验收恢复点不变。

## 代码与已完成开发

唯一活动工作区：`T:/Windows/Documents/GitHub/TraceFold`。

接续分支 `codex/main-workspace-20261023`，整合基线HEAD `1062ce881665c9c7f44f42f158b2e9f34d30f735`。该合并提交的双亲为原main的e3e87e4及开发线e698861；保留原main三项安全修复，双方修改路径不重叠、无合并冲突。本次只核对Git对象和文件字节，没有运行整合后的项目测试。main引用仍为e3e87e4，未push。

| 已核收开发成果 | 提交 |
|---|---|
| Task40、Task50 | 8bd7c66、2e3f298，保留原限定验收 |
| Metadata基础/生产接线、主题生产者S123 | b578841、a1abcded、3285dc9 |
| 离线报告索引 | 3b446bf→90e6799 |
| 离线包入口 | b79917e |
| 分离产包/ZIP/事务代码 | 0828fc3e |
| 包根启动器、指南、初始验收表 | e698861d |

这些提交已在当前HEAD祖先链；不表示Task13真实矩阵或最终Stage5CC发布通过。当前是工作分支整合，未合并到main。

## 当前未完成：Task13

从临时接续worktree逐件迁入19件：16M+3新。当前19件字节与e698861上的冻结候选及迁移快照一致；新增AGENTS和docs/codex文档另计。原候选和临时接续worktree均保留原样，没有新Task13实现提交。

最近旧工作流核收：toolkit All3两宿主各392/392、native0；Visual62/62、Workflows32/32。它们是开发回归，不能证明公开入口到完整70正向整链。

独立R1仍未通过，四项待处理：

1. **核心整链**：源码与整体移动包，Both/CurrentOnly，经公开-File→真实70顶层→Runner/Evidence唯一出口的正向受控验证。只替OS/Driver命令末端与非theme Support，保留真实编排/断言；真实theme仅fixture适配器，产品观察标Synthetic。
2. startup最后readback漏ExpectedAdapter：第三答与fixture不匹配仍可能误确认。
3. stdin超时Kill后缺实际退出/双流收口观察和强制断言，不能拿测试自己清理holder冒充wrapper完成。
4. bootstrap测试若目录已存在，Force覆盖却不恢复；须准确保全/恢复测试前状态。

CurrentOnly自动恢复写入与Synthetic投影在待完成候选中已修，不再当成尚未实施。指南/头注“真实Support未执行”也不准确：真实CLI已在隔离fixture phases执行，未验证production注册表/广播/UI。材料旧缺档/矛盾照实保留，不追补历史原流。

当前stdin物理代表允许使用确切持有、已观察退出的cmd Process，经同一生产发送函数写真实管道；不证明仍活子进程CloseHandle。旧csc夹具拒绝及历史红例保持记录，不重开或换编译器完成它。

## 剩余大块与边界

Task13开发闭包及之后的真实主题/布局/键鼠矩阵；Task12的61自动验收；Task14最终包/完整真实Scenario All及统一整合仍未完成。61人工重复启动回前台的有限确认保留，旧正式自动失败不改成Passed。

迁移后先完成当前开发闭包，再决定真实验证/整合范围。允许的真实Support fixture能力不等于允许production adapter、真实产品/driver/桌面/主题操作；当前全部暂停，待用户明确继续。

## 必要原件

原K=`T:/TraceFold5CC/.superpowers/development/T13-VISUAL-INT-01`：RESULT.md、reviews/VISUAL-INT-01-R1-GPT/REVIEW.txt、attempts/all3-*、t/tmp中的CLI/publish证据保留。
回归主目录记录=`T:/Windows/Documents/GitHub/TraceFold/.superpowers/codex-transition/20261023-return-main`：PREPARATION.json、source-files、迁移前main index及最终核对。前一轮迁移根=`T:/TraceFold5CC/.superpowers/codex-transition/20261023`：SOURCE-SNAPSHOTS.json、backups、NEW-WORKSPACE.json、COORDINATOR-HANDOFF.md。旧NEW-WORKSPACE记的是中间落点，以本页当前活动路径为准。无需每次重新扫描整树或加载9627字节交接全文。
