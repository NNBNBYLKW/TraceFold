下面是 **`WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH`** 的正式任务文档初稿。

这一步只负责把 **Workbench / Dashboard、Knowledge detail、Health** 三条主消费线的共用页面壳层收口：统一 **page shell / section shell / state block / source reference block / action row** 的语义和位置规律，目标是减少页面漂移、增强“一个工作台”的感觉，同时严格保持当前边界：这不是 design-system rewrite，不是新的 global state platform，不是全站组件大改，也不新增 AI center、alerts center、task runtime control center 一类新中心。当前 shared state 基线已经明确这轮工作应聚焦 `loading / empty / unavailable / degraded`、derivation 状态和 alerts 状态的一致表达；现有正式覆盖页仍是 Workbench / Dashboard、Knowledge detail + `knowledge_summary`、Health records + alerts。

---

# `WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH`

## 1. 任务定位

本任务是 **Post-Phase-1 Web 前端优化** 的横向壳层收口任务。

它解决的问题不是“页面还没做”，而是：

* 已经成立的主消费线在页面结构、区块层级、状态表达、动作位置上仍可能存在漂移
* 用户需要感受到这是 **一个统一的个人数据工作台**
* 前端要强化统一交互语言与低心智切换成本，而不是让每个页面各自长成一套语义。

本任务必须服务于当前已冻结的软件形态：

* Web 是主要业务界面，但不是业务真逻辑中心
* Desktop 只是桌面壳入口，不是另一套业务客户端
* 多入口必须服务于同一主线，而不能各长一套语义。

---

## 2. 任务目标

本任务的正式目标是：

> **在不扩张功能边界、不引入新产品中心的前提下，为当前三条正式 Web 主消费线建立一套克制、稳定、可复用的共用页面壳层，使页面在层级、状态、动作、上下文支撑上更一致、更像一个工作台。**

拆开后包含五个子目标：

### 2.1 统一页面首层结构

让三条主线都更容易呈现：

* 页面标题 / 说明
* primary section
* secondary section
* contextual support
* state block
* local action row

而不是每页自由生长。当前 shared state baseline 已经强调“语义一致性优先于完全同形”，所以这里追求的是 **结构规律统一**，不是卡片样式完全相同。

### 2.2 统一 section 壳层语义

让页面中的主要区块更明确地区分：

* primary truth-bearing section
* secondary derived / reminder section
* contextual metadata / source block
* local state block
* small local action row

这与当前 hierarchy contract 一致：formal facts 仍应优先于 derived layers，actions 应晚于理解，state 应尽可能局部化。

### 2.3 统一 shared state block 的出现方式

当前 baseline 已经明确：

* `loading`
* `empty`
* `unavailable`
* `degraded`

以及 derivation / alerts 的细分语义。这个任务要解决的是 **它们出现的位置、版面重量、与正文关系**，而不是改写状态定义本身。

### 2.4 统一 source/context 支撑区

Knowledge 与 Health 已经有 `Source Reference` 的页面存在感；Workbench 也可能有 runtime / freshness / explanatory context。这个任务应把这类“支持理解但不抢主位”的区块做成稳定的 contextual shell。

### 2.5 统一小动作的承载方式

当前允许的小动作包括：

* `Recompute AI-derived Summary`
* `Acknowledge Alert`
* `Resolve Alert`

这些动作都已被冻结为 **小而局部**，不能因为做 shared shell 就把它们抬升成控制台式动作条。

---

## 3. 明确非目标

本任务 **不做** 下列事情：

* 不做 design-system rewrite
* 不做新的 global state platform
* 不做 full-site component overhaul
* 不做全站样式统一工程
* 不新造 Web-owned business workflow
* 不新增聚合 API
* 不把 Workbench 做成第二业务中心
* 不把 Knowledge 做成 AI center
* 不把 Health 做成 alerts center
* 不新增 Desktop / Telegram 专属展示语义。

这点非常关键。技术路线已经明确：界面层负责展示状态、发起操作、承载交互、呈现 AI 与正式数据的区别、降低心智负担；它不应承担解析逻辑、规则判断、正式数据写入策略或业务流程主判断。

---

## 4. 本任务的输入边界

本任务的直接输入应只依赖当前已冻结文档和已成立页面：

* `WEB_CONSUMPTION_BASELINE.md`
* `WEB_SHARED_STATE_POLISH_BASELINE.md`
* `WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
* `WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
* 你刚刚冻结的：
  * `WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
  * `WEB_INFORMATION_HIERARCHY_CONTRACT.md`

从正式 Web 基线看，当前覆盖页面与 API 仍是：

* Workbench / Dashboard：`/api/workbench/home`、`/api/dashboard`、`/api/system/status`
* Knowledge detail：`/api/knowledge/{id}`、`/api/ai-derivations/knowledge/{id}`、`POST recompute`
* Health：`/api/health`、`/api/health/{id}`、`/api/alerts`、`POST acknowledge/resolve`

所以这一步应是 **presentation shell 收口**，不是数据边界扩张。

---

## 5. 建议输出物

建议本任务最终输出为三类结果。

### 5.1 一个轻量 shared presentation 层

建议只覆盖当前三条主线真正重复出现的壳层，不追求通用到“未来所有页面”。

建议最小集合：

* `PageShell`
* `PageHeaderBlock`
* `SectionShell`
* `StateBlock`
* `SourceReferenceBlock`
* `SectionActionRow`

命名可调整，但职责不要扩。

### 5.2 一份任务完成文档

建议新增：

`docs/WEB_SHARED_PAGE_SHELL_POLISH_BASELINE.md`

作用是记录：

* 这次 shared shell 收了哪些壳层
* 它们分别解决什么一致性问题
* 哪些明确没做
* 需要跑哪些 smoke / tests

### 5.3 必要的前端测试补强或对齐

当前仓库中已存在与 shared semantics / workbench / knowledge / health 相关的测试入口，包括：

* `apps/web/tests/test_semantics_contract.py`
* `apps/web/tests/test_workbench_home_contract.py`
* `apps/web/tests/test_knowledge_ai_ui_contract.py`
* `apps/web/tests/test_health_ai_ui_contract.py`
* `apps/web/tests/shared/test_shared_state_polish.py`
* 以及 knowledge / health page-specific tests。

本任务优先沿用和补强现有测试，不另起验证体系。

---

## 6. 具体执行步骤

## Step A：先收“共用壳层清单”，不急着抽象

先盘点三条主线真正共同出现的结构元素，只纳入 **已经跨页重复且语义稳定** 的部分。

建议纳入：

* 页面标题区
* 页面说明区
* 主区块壳
* 次区块壳
* 局部状态块
* source/context 支撑块
* 小动作行

建议暂不纳入：

* 页面级 filters
* 列表分页 controls
* 复杂卡片组合
* 顶部导航 / 侧边导航
* 图表组件
* 特殊 domain-specific widgets

原因很简单：shared shell 要解决的是“页面读起来像同一系统”，不是“所有前端东西都抽一层”。

## Step B：先定每个壳层的单一职责

每个壳层只回答一个问题。

例如：

* `PageShell`：控制页面主宽度、垂直节奏、总体结构容器
* `PageHeaderBlock`：页面名、简短说明、必要的顶层 contextual info
* `SectionShell`：区块标题、区块说明、内容容器、局部状态挂载位
* `StateBlock`：展示 `loading / empty / unavailable / degraded` 或 derivation/alerts 局部状态
* `SourceReferenceBlock`：展示来源与相关上下文，但不抢主位
* `SectionActionRow`：承载本区块的轻量动作

职责不应混写，否则很容易从 shell 收口滑成页面逻辑混合层。

## Step C：统一 state block 的位置与重量

shared state baseline 已经把语义冻结了，这一步只需要把视觉出现规律统一：

* primary read 失败 → 用 page-level unavailable
* secondary section 失败 → 用 section-level unavailable
* `degraded` → 尽量以 local support block 出现，不打断 primary content
* `empty` 和 `unavailable` 必须明显不同
* derivation state 不能伪装成 formal detail state
* alerts state 不能伪装成 formal record state

## Step D：统一 contextual block 的位置

`Source Reference`、runtime explanation、derivation metadata、alert lifecycle labels 这些东西都属于 contextual support。

要点是：

* 可以帮助理解
* 不能打断 primary reading flow
* 不应在视觉重量上压过正式内容或主摘要

Knowledge 和 Health 的现有基线都已经把这一点写得很清楚。

## Step E：统一小动作的载体

小动作保留，但不放大。

建议规则：

* 本区块动作跟随区块，不提到页首
* 动作文案明确，避免控制台感
* 动作结果回到当前页语义，不额外打开“任务中心感”的过渡面

这特别适用于：

* `Recompute AI-derived Summary`
* `Acknowledge Alert`
* `Resolve Alert`

## Step F：分别落到三条页面线上验证

shared shell 完成后，不要先追求“漂亮”，先看三页是不是更稳定：

* Workbench 是否更像入口工作台，而不是平铺 dashboard
* Knowledge 是否更清楚地区分 formal vs AI
* Health 是否更清楚地区分 formal records vs rule alerts

---

## 7. 对三条主线的预期影响

### 7.1 对 Workbench / Dashboard

预期提升：

* 首屏更像“当前系统状态 + 下一步入口”
* `degraded` 更容易局部挂载
* summary blocks 不再像无差别堆卡片

但明确不做：

* 不新增 dashboard center
* 不新增 control surface
* 不新增 runtime management 行为

### 7.2 对 Knowledge detail

预期提升：

* `Formal Content` 与 `AI-derived Summary` 的层级更稳定
* derivation states 更易区分
* `Recompute` 位置更自然、更轻

但明确不做：

* 不做 AI panel
* 不做 model/prompt controls
* 不让 derived text 视觉上凌驾 formal truth

### 7.3 对 Health

预期提升：

* `Formal Records` 与 `Rule Alerts` 更容易分层理解
* alerts lifecycle grouping 更好读
* `alerts empty` / `alerts unavailable` 更容易区分

但明确不做：

* 不做 alerts center
* 不做 medical assistant UI
* 不做 rule configuration 面板

---

## 8. 验收标准

本任务完成时，应满足以下条件：

1. 三条主线页面都有更稳定的：
   * header
   * section
   * state
   * contextual
   * local action
     结构规律

2. shared state 语义没有变化，但更容易被看懂：
   * `loading`
   * `empty`
   * `unavailable`
   * `degraded`
   * derivation states
   * alerts states

3. Workbench / Knowledge / Health 三页更像同一系统，而不是三种独立页面风格。这个目标与“统一 Web 界面技术栈、统一交互语言、降低多端分裂”的技术路线一致。

4. Formal facts 仍然是主位；AI 与 alerts 仍然是 secondary layers。

5. 没有引入新的业务中心、控制台或平台化壳层。

6. 现有相关测试继续通过，必要时只做局部补强，不另起新框架。

---

## 9. 防偏航检查

做这个任务时，任何改动只要出现下面任一趋势，就要停下来重写：

* 为了“统一”开始做全站组件体系重构
* 为了“更顺手”往页面壳里塞业务逻辑
* 为了“更完整”新增 Web-only 聚合 API
* 为了“更可操作”把小动作做成控制台
* 为了“更高级”把 AI 区块抬成页面主角
* 为了“更集中”把 alerts 做成操作中心
* 为了“照顾入口差异”长出 Desktop-only / Telegram-only 语义

这类方向都和软件最终设想冲突，因为你要的是 **统一底座、统一主链、统一工作台感**，不是每个入口和每个页面都各自进化。

---

## 10. 推荐验证方式

沿用当前已有的 Web smoke 和 tests 即可。

### Manual smoke

* 打开 `/workbench`，确认 header / summary / runtime state / degraded block 的相对位置更清楚
* 打开任一 knowledge detail，确认 `Formal Content`、`Source Reference`、`AI-derived Summary` 的层级更清楚
* 打开 `/health` 与 detail，确认 `Formal Records`、`Rule Alerts`、lifecycle grouping 与状态块更清楚

### Automated checks

优先继续跑当前已有测试组合：

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py