# STEP 7 ENTRY CONTRACTS

## Purpose

本文档用于定义 Step 7 的多入口职责契约，明确 Telegram、Desktop Shell、Web Workbench、Service/API 在当前阶段分别能做什么、不能做什么，以及允许的调用路径与禁止的绕路方式。

本文档服务于实现约束与 code review。若某个入口实现与本文档冲突，默认以本文档为准。

## Contents

- Contract Overview
- Telegram Contract
- Desktop Shell Contract
- Web Workbench Contract
- Service/API Contract
- Forbidden Shortcuts
- Review Notes

---

## Contract Overview

Step 7 的多入口关系固定如下：

- Telegram 是轻入口
- Desktop 是桌面壳
- Web 是主要业务界面
- Service/API 是唯一业务语义中心

Step 7 只有一条允许的总体原则：

**不同入口可以有不同接入形式，但不能形成不同系统。**

因此：

- Telegram 只负责轻量输入、轻量查看、轻量触发
- Desktop Shell 只负责应用壳层承接
- Web 继续承担主要业务工作面
- 所有真实业务判断、状态流转、正式写入语义都收口到统一 service/API

## Telegram Contract

Telegram 在 Step 7 中仅作为轻入口存在。

Allowed:

- Telegram may submit captures via approved service/API path
- Telegram may view minimal pending/dashboard/alerts summaries
- Telegram may trigger approved pending actions
- Telegram fix is allowed only as single-turn minimal text correction

Not allowed:

- Telegram must not define parsing rules
- Telegram must not define pending review rules
- Telegram must not define formal-write rules
- Telegram must not become a parallel management client
- Telegram `force_insert` is not allowed in Step 7

补充说明：

- Telegram 的所有动作都必须通过统一批准的 service/API 路径进入系统
- Telegram 不拥有独立业务状态机
- Telegram 不承担完整详情阅读、复杂页面替代、批量管理或多轮工作流职责
- Telegram 的 `fix` 只能是单轮、单条、极简文本修正，最终结果仍由统一 pending review service 决定

允许路径：

- Telegram -> API router -> application service -> repository / persistence

禁止路径：

- Telegram -> direct DB
- Telegram -> private review logic
- Telegram -> private formal-write path

## Desktop Shell Contract

Desktop 在 Step 7 / Phase 1 中仅定义为桌面壳。

Allowed:

- Desktop shell is an application-style wrapper
- It may provide tray / resident / shortcut / service visibility / notification bridge
- It may open the Web workbench or specific workbench pages
- It may bridge limited system-level capabilities

Not allowed:

- It must not host an independent business-domain UI
- It must not own review logic
- It must not write database directly
- It must not run independent rule or AI workflows

补充说明：

- Desktop Shell 的职责是承接系统风格入口，不是替代 Web Workbench
- Desktop Shell 可以作为启动器、驻留壳、状态面板和最小通知桥接层
- Desktop Shell 不拥有独立 Pending 逻辑，不拥有正式写入语义，也不拥有独立 AI 回写能力

允许路径：

- Desktop Shell -> open Web Workbench
- Desktop Shell -> API router -> application service

禁止路径：

- Desktop Shell -> direct DB
- Desktop Shell -> private state transition
- Desktop Shell -> private rule workflow

## Web Workbench Contract

Web workbench 是 Step 7 的主要业务界面，也是默认的完整工作台。

核心职责：

- Web remains the main business interface
- Detailed domain views stay in Web
- Desktop shell hosts or opens Web instead of replacing it

补充说明：

- 复杂查看、复杂确认、主要业务工作流仍由 Web 承担
- Telegram 和 Desktop Shell 可以把用户导回 Web，但不能取代 Web
- Web 也不能因为是主工作台就绕过 service 层形成专属业务语义

## Service/API Contract

Service/API 是 Step 7 中唯一可信的业务语义中心。

必须满足：

- service layer is the only business logic center
- router stays thin
- repository stays persistence-only
- no entry may define its own business-state transition

工程化要求：

- 所有入口统一通过 API router 进入应用服务层
- router 只负责接入、参数接线、响应返回，不承载业务真逻辑
- service 层负责规则判断、状态流转、结果决定和正式写入语义
- repository 只负责持久化读写，不承载业务规则
- 任一入口都不允许定义自己的业务状态流转
- 任一入口都不允许定义自己的正式写入判定
- 相同业务动作在不同入口必须得到相同业务结果

当前章节只定义合同，不展开接口字段、返回结构或实现细节。

## Forbidden Shortcuts

Step 7 中明确禁止以下捷径：

- bot direct DB writes
- desktop direct DB writes
- entry-specific pending logic
- entry-specific formal-write semantics
- entry-owned rule evaluation
- entry-owned AI write-back
- duplicate state transition logic outside service layer

补充禁止项：

- Telegram 暴露 `force_insert`
- Desktop 通过壳层私有逻辑绕开 API
- Web 为了局部交互便利复制 service 层状态判断
- 任一入口在 service 层之外拼接正式结果

这些做法即使短期可用，也会破坏 TraceFold 的统一骨架，不属于 Step 7 可接受方案。

## Review Notes

后续 code review 可使用以下短检查表：

- 该改动是否只改变入口交互，而未改变业务真语义
- Telegram 是否仍然只是轻入口，而不是并行管理端
- Desktop Shell 是否仍然只是桌面壳，而不是第二业务前端
- Web 是否仍然保持主要业务界面定位
- router 是否仍然轻薄
- service 是否仍然是唯一业务逻辑中心
- repository 是否仍然只负责持久化
- 是否出现 direct DB、私有状态流转、私有规则判断或私有 AI 回写

更详细的评审项见 `STEP7_REVIEW_CHECKLIST.md`。
