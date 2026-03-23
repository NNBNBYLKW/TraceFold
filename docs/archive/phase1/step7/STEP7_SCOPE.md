# STEP 7 SCOPE

## Purpose

本文档用于冻结 Phase 1 / Step 7 的范围边界，明确本步骤的目标、纳入范围、非范围、边界原则、偏航风险和完成标准。

本文档是 Step 7 的方向基线。后续若实现、任务说明或评审结论与本文档冲突，默认以本文档为准，除非先明确更新本文档。

## Contents

- Objective
- Why Step 7 exists
- In Scope
- Out of Scope
- Boundary Principles
- Drift Risks
- Completion Criteria

---

## Objective

Step 7 的目标不是“做两个新端”，而是在多入口接入后，继续验证 TraceFold 仍然只有一套业务主线。

当前目标聚焦于：

- 验证多入口接入后，系统仍然沿同一条 `Capture -> Parse -> Pending -> Confirm -> 正式记录 -> 查询 / 分析 / 派生解释` 主链运行
- 为 Telegram 提供轻入口能力，而不是建立第二业务前端
- 为 Desktop 提供桌面壳能力，而不是建立第二套业务界面
- 保持 Web/API 仍然是主要业务承载面
- 继续把应用服务层保持为唯一业务真逻辑中心

## Why Step 7 exists

Step 7 存在的原因，是让 TraceFold 更接近日常可用的个人工作台形态，同时避免随着多入口接入而发生业务逻辑分裂。

Step 7 解决的不是“再做几个入口”，而是以下问题：

- Telegram 是否只承担轻入口职责
- Desktop 是否只承担桌面壳职责
- Web/API 是否仍然承担主要业务承载
- 同一条记录、同一个 pending 状态、同一类提醒是否仍然保持统一语义
- 多入口接入后是否仍然只有一套业务逻辑，而不是多个入口各自演化

## In Scope

Step 7 当前纳入范围仅包括以下方向：

- Telegram capture input
- Telegram minimal pending visibility
- Telegram minimal pending action
- Telegram lightweight dashboard / alerts visibility
- Desktop shell as system-style entry wrapper
- Minimal notification bridge
- Cross-entry consistency validation

具体说明如下：

- Telegram 只承接轻量输入、轻量查看、轻量触发动作
- Desktop 只承接系统风格入口包装、状态可见和最小通知桥接
- Web 继续作为主要业务工作台
- Step 7 的验证重点是跨入口语义一致，而不是入口能力做得更重

## Out of Scope

Step 7 当前明确排除以下方向：

- Full knowledge reading in Telegram
- Long AI summary rendering in Telegram
- Complex health page replacement in Telegram
- Batch operation in Telegram
- Multi-turn fix workflow in Telegram
- `force_insert` exposed in Telegram
- Heavy native desktop business UI
- Desktop-owned business state logic
- Notification platform / automation engine
- Entry-specific formal-write semantics
- Any independent business logic outside service layer

补充冻结结论如下：

- Telegram `fix` 仅限单轮、单条、极简文本修正
- Telegram 不做字段级表单
- Telegram 不做多轮会话状态机
- Desktop 不直接写库
- Desktop 不承载独立规则判断
- Desktop 不承载独立 AI 流程
- 任一入口都不允许绕过统一 pending review service 决定结果

## Boundary Principles

Step 7 必须遵守以下边界原则：

1. one service core
2. one truth source
3. one business semantics
4. different entry forms, not different systems
5. desktop shell is shell, not business layer
6. Telegram is lightweight entry, not main workbench

工程化解释如下：

- 应用服务层是唯一业务真逻辑中心
- SQLite 与统一正式记录链路仍然是真相源
- 相同业务动作在不同入口必须保持同义
- 入口差异只能体现在交互形式，不允许体现在业务规则上
- Desktop 的职责是承接系统能力，不是复制业务层
- Telegram 的职责是轻入口，不是承担主要工作台职责
- Web/API 仍然是主要业务承载面

## Drift Risks

Step 7 的主要偏航风险包括：

- bot becomes a shortcut backend
- desktop shell grows hidden business logic
- temporary workaround becomes architecture
- notification layer becomes second workflow center
- entry-specific state semantics appear

这些风险在 Step 7 中通常表现为：

- Bot 为了方便而直接承接本应由 service 层决定的动作
- Desktop 为了承接更多体验而隐藏新增业务判断
- 临时绕过 API 的方案被保留下来并变成长期结构
- 通知层开始承担提醒之外的流程驱动职责
- Telegram、Desktop、Web 对同一状态出现不同解释

评审时应优先识别这些偏航信号，而不是只判断功能是否可演示。

## Completion Criteria

Step 7 的完成标准不是功能数量，而是系统统一性已经被保住。

至少应满足：

- same record semantics across entries
- same pending semantics across entries
- same alert meaning across entries
- no special backdoor entry
- no bypass of service layer
- Step 7 judged by system unity, not feature count

等价的工程判断如下：

- 同一条记录在 Telegram、Desktop、Web 所触达的结果语义一致
- 同一个 pending 状态在不同入口没有不同解释
- 同一类提醒在不同入口表达形式可不同，但含义一致
- 没有任何入口成为可绕开正式流程的特殊后门
- 没有任何入口绕过 service 层直接形成正式业务结果
- Step 7 的通过依据是统一骨架成立，而不是新增入口功能数量

本文档后续应与以下文档配套使用：

- `STEP7_ENTRY_CONTRACTS.md`
- `STEP7_ERROR_AND_FEEDBACK.md`
- `STEP7_ACCEPTANCE.md`
- `STEP7_REVIEW_CHECKLIST.md`
