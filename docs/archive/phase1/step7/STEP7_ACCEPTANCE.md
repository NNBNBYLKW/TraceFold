# STEP 7 ACCEPTANCE

## Purpose

本文档用于定义 Step 7 的验收口径，确保验收重点落在“系统是否仍然统一”而不是“新增了多少功能点”。

## Contents

- Acceptance Purpose
- Core Acceptance Principle
- Scenario-Based Acceptance
- Boundary Protection Checklist
- Non-Goals Verification
- Manual Validation Notes

---

## Acceptance Purpose

Step 7 is accepted when multi-entry access strengthens system unity instead of creating parallel systems.

换言之，Step 7 的验收目标，是确认多入口接入后 TraceFold 仍然保持统一骨架、统一主链、统一服务路径和统一状态语义，而不是因为新增入口而分裂成多个业务系统。

本文件不展开自动化测试设计，只定义当前阶段的人审与场景验收框架。

## Core Acceptance Principle

Step 7 的核心验收原则如下：

**不以功能多少验收，而以系统统一性验收。**

至少应满足以下一致性：

- same flow
- same source of truth
- same business semantics
- same service path
- different entry forms only

工程化解释如下：

- 多入口必须沿同一主链运行，而不是各自拥有不同流程
- 不同入口必须仍然指向同一真相源
- 同一条记录、同一个 pending 状态、同一类 alert 在不同入口必须保持同义
- 所有入口必须复用同一 service/API 路径
- 入口之间允许交互形态不同，但不允许业务语义不同

## Scenario-Based Acceptance

Step 7 可按以下场景进行人工验收：

### Scenario A: Telegram Capture Path Consistency

场景：

- Telegram submit capture -> system accepts it through approved path -> resulting state is visible in Web

验收点：

- Telegram 可发起 capture 输入
- 该输入通过批准的 service/API 路径进入系统
- 没有形成 Telegram 私有写入路径
- 结果状态可在 Web 中按统一语义查看

### Scenario B: Pending State Consistency Across Entries

场景：

- Pending viewed in Web -> pending action triggered through approved path -> resulting state is reflected consistently across entries

验收点：

- Pending 在 Web 中可见
- Pending 动作通过批准路径触发
- 动作结果不依赖入口特例
- 结果状态在不同入口中保持一致含义

### Scenario C: Telegram Minimal Fix Uses Service Review

场景：

- Telegram minimal fix -> service-layer review logic processes it -> resulting pending state remains service-defined, not Telegram-defined

验收点：

- Telegram `fix` 仍然是单轮、单条、极简文本修正
- Telegram 不定义 review 规则
- review 结果由统一 service 层产生
- resulting pending state 仍然是 service-defined，而不是 Telegram-defined

### Scenario D: Desktop Shell Opens Shared Workbench Context

场景：

- Desktop shell opens the same workbench context instead of owning a separate business screen

验收点：

- Desktop Shell 可打开同一 Web workbench 或指定 workbench 页面
- Desktop Shell 不拥有独立业务屏幕语义
- Desktop Shell 只是承接入口，不是第二业务前端

### Scenario E: Health Alert Meaning Stays Unified

场景：

- High-priority health alert remains attached to the same formal health record and keeps the same meaning across entries

验收点：

- 高优先级 health alert 指向同一正式 health 记录语义
- 不同入口的呈现可以不同，但 alert meaning 不变
- 没有入口为 alert 创造私有解释或私有处理规则

### Scenario F: Service Unavailable Is Visible Without Hidden Write Path

场景：

- Service unavailable state is surfaced clearly, with no hidden fallback write path

验收点：

- 服务不可达时，状态能被明确感知
- 没有隐藏 fallback write path
- 没有本地临时事实补写
- 没有入口私自伪造“稍后同步成功”的结果

## Boundary Protection Checklist

验收时至少确认以下问题都可回答为 `Yes`：

- Yes / No: no bot direct DB write
- Yes / No: no desktop direct DB write
- Yes / No: no Telegram `force_insert` exposure
- Yes / No: no entry-specific pending rule
- Yes / No: no entry-specific formal-write rule
- Yes / No: no hidden fallback storage
- Yes / No: no second workflow center

只有当以上检查均可回答为 `Yes`，Step 7 的边界保护才可视为成立。

## Non-Goals Verification

Step 7 验收还必须确认以下非目标没有被带入：

- Step 7 did not become a full notification platform
- Step 7 did not become a desktop-native rewrite
- Step 7 did not turn Telegram into a heavy management client
- Step 7 did not create an entry-specific review workflow

补充检查口径：

- 没有把通知层做成复杂自动化中心
- 没有把桌面壳做成第二套原生业务前端
- 没有把 Telegram 做成重型管理入口
- 没有在任一入口建立私有 review 工作流

## Manual Validation Notes

当前阶段建议保留人工验证说明位，后续按真实实现补充。

建议至少记录：

- 验证日期
- 验证入口
- 验证场景
- 预期语义
- 实际结果
- 是否发现入口特例或语义漂移
- 是否存在隐藏写入路径
- 是否发现 service 层绕过

本章节当前只作为手工验收占位，不展开具体脚本和测试命令。
