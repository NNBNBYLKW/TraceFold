下面是整理好的 **可直接复制** 版本。

# `WEB_PP1_TASK8_CROSS_PAGE_CONSISTENCY_AND_SMOKE`

## 1. 任务定位

本任务是 **Post-Phase-1 Web 前端优化** 的最终收口任务。

它解决的问题不是“某一页怎么再变强”，而是：

- 前面各条页面优化任务即便都做完，也仍可能存在 **跨页语义漂移**
- 文档、shared state、manual smoke、automated checks 需要形成一个 **统一闭环**
- 当前阶段的完成标准不应是“功能看起来更多”，而应是 **系统感更稳、边界更清楚、验证路径更统一**

---

## 2. 任务目标

本任务的正式目标是：

> **在不引入新产品中心、不增加新平台层、不改动当前正式 Web 消费边界的前提下，把 Workbench / Knowledge / Health 三条已成立主消费线在层级语言、状态语义、动作重量、页面角色和验证路径上做一次统一收口，形成可交付、可回归、可持续维护的 Post-Phase-1 Web 优化闭环。**

拆开后包含六个子目标。

### 2.1 统一页面角色语言

当前三条线的页面角色已经成立：

- Workbench = entry layer
- Dashboard = summary layer
- Knowledge detail = formal record read + secondary AI interpretation
- Health = formal record read + secondary reminder layer

这一步要做的是检查它们在实际页面和文档里是否仍然表达一致，而不是让页面继续各说各话。

### 2.2 统一 primary / secondary / state 的跨页语义

当前 shared state baseline 已明确：

- `loading / empty / unavailable / degraded`
- derivation 的 `failed / invalidated / not generated`
- alerts 的 `empty / unavailable`

都应跨页保持一致意义，且这轮目标是“语义一致”，不是“所有页面完全同形”。

### 2.3 统一“小动作不是控制台入口”的表达

当前 Knowledge 只保留一个小动作 `Recompute AI-derived Summary`，其含义被明确限制为请求重算派生结果，不改变 formal record，也不把页面变成 task center 或 AI control surface。Health 的 acknowledge / resolve 也应继续保持局部生命周期动作，而不是工作流控制面。

### 2.4 形成文档闭环

`WEB_CONSUMPTION_BASELINE.md` 已经明确推荐了 Web 文档流：先 consumption baseline，再 shared state，再 Knowledge / Health 低层基线，再走 seeded integration smoke。这个任务要把前面新增的 Post-Phase-1 文档，与这套正式流衔接起来。

### 2.5 形成 smoke 闭环

当前 shared state 与 Knowledge 基线都已经给出明确的 manual smoke 和 automated verification 路径；本任务要把这些路径整合成 **一套收口检查顺序**，而不是分散在几份文档里。

### 2.6 确保不偏离原有最终设想

最终软件设想强调：这个系统是 **统一主线、统一底座、统一工作台感** 的个人数据平台，不应因为某个单页优化而滑向万能平台、AI center、健康助手、图谱中心或自动化平台。

---

## 3. 明确非目标

本任务 **不做**：

- 不新增页面能力
- 不新增 Web-only 聚合 API
- 不新增设计系统项目
- 不新造 global state platform
- 不把 Workbench / Knowledge / Health 重新设计成三套局部产品
- 不新增 AI center / alerts center / rule console / task runtime control center
- 不引入 Desktop / Telegram 专属页面语义
- 不把模板推成自动化引擎

这点必须继续卡死，因为技术路线要求入口只能接入统一服务能力，界面层只负责展示状态、发起操作、承载交互、降低心智负担，不能自己长出业务真逻辑。

---

## 4. 本任务直接依赖的基线

本任务应只依赖当前已经冻结的 Web 正式基线与项目级边界：

- `WEB_CONSUMPTION_BASELINE.md`
- `WEB_SHARED_STATE_POLISH_BASELINE.md`
- `WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
- `WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
- 以及前面刚规划的 PP1 Web 优化文档序列

从正式基线看，这一轮 Web 范围仍然是：

- Workbench / Dashboard
- Knowledge detail + `knowledge_summary`
- Health records + rule alerts
- shared state presentation

---

## 5. 建议输出物

### 5.1 一份最终收口文档

建议新增：

`docs/WEB_PP1_CROSS_PAGE_CONSISTENCY_AND_SMOKE_BASELINE.md`

这份文档只做四件事：

- 列出当前纳入收口的页面
- 列出必须一致的跨页语义
- 给出最终 manual smoke 顺序
- 给出最终 automated verification 命令

### 5.2 基线文档回填

建议对前面新增的几份文档做最小回填：

- 在 `WEB_POST_PHASE1_OPTIMIZATION_PLAN.md` 中标记 Task 8 为 closure step
- 在 `WEB_INFORMATION_HIERARCHY_CONTRACT.md` 中标记 cross-page consistency 由 Task 8 最终校验
- 在各页任务文档里补“最终以 Task 8 为统一验收出口”

当前实现建议继续保持克制：

- 优先补 Task 8 closure doc
- 只做最小必要的回指说明
- 不把 Task 8 再扩成新的实现波次

### 5.3 统一 smoke 清单

建议把当前分散在 shared state、Knowledge 和 Health 文档中的 smoke 路径，整理成一份统一顺序清单。现有基线已经明确：

- `/workbench` 需验证 summary + runtime status
- knowledge detail 需验证 `Formal Content` 与 `AI-derived Summary` 分离
- `/health` 需验证 `Formal Records` 与 `Rule Alerts` 分离

---

## 6. 具体执行步骤

### Step A：先冻结“当前已纳入收口的页面”

只纳入当前正式覆盖的三条主消费线：

1. Workbench / Dashboard
2. Knowledge detail + `knowledge_summary`
3. Health records + rule alerts

不要顺手把 Expense、Capture、Pending、Templates 一并拖进这轮 closure。现在做的是 **已完成消费线的收口**，不是整站大验收。

### Step B：逐项检查跨页一致性

这一步只检查四类一致性。

#### 1）页面角色一致性

- Workbench 是否仍像 entry layer
- Dashboard 是否仍像 summary layer
- Knowledge 是否仍像 formal-first record page
- Health 是否仍像 formal-first + alerts-secondary page

#### 2）状态语义一致性

- `loading / empty / unavailable / degraded` 是否仍是同一套含义
- derivation 状态是否仍挂在 AI section 内
- alerts 状态是否仍挂在 alerts section 内

#### 3）主次关系一致性

- formal facts 是否在三页里都仍然是 primary
- AI / alerts / runtime 是否都仍是 secondary 或 support layer
- `degraded` 是否继续以局部支持块为主，而不是整页 collapse

#### 4）动作重量一致性

- `Recompute AI-derived Summary` 是否仍是小动作
- acknowledge / resolve 是否仍是小动作
- 首页入口是否仍是“进入上下文”而不是控制面按钮墙

### Step C：做文档层统一

把当前规划文档与正式基线串成一个清晰顺序。

建议最终阅读顺序保持为：

1. `WEB_CONSUMPTION_BASELINE.md`
2. `WEB_SHARED_STATE_POLISH_BASELINE.md`
3. `WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
4. `WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
5. `WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
6. `WEB_INFORMATION_HIERARCHY_CONTRACT.md`
7. `WEB_PP1_CROSS_PAGE_CONSISTENCY_AND_SMOKE_BASELINE.md`

也就是：**正式现状基线在前，PP1 优化规划与最终 closure 在后**。这样不会把优化文档误写成新的系统定义文档。

### Step D：做统一 manual smoke

建议把 manual smoke 固定成这个顺序：

1. 打开 `/workbench`  
   确认 workbench home、dashboard summary、runtime status 可读，并验证 `ready / degraded / unavailable` 的层级是否清楚。

2. 打开一个 knowledge detail  
   确认 `Formal Content`、`Source Reference`、`AI-derived Summary` 分离；如有条件，再检查 `not generated / failed / invalidated` 之类的 derivation wording 是否仍清楚。

3. 打开 `/health` 与任一 health detail  
   确认 `Formal Records` 与 `Rule Alerts` 分离，且 `alerts empty` 与 `alerts unavailable` 不会被呈现成同一状态。

### Step E：做统一 automated verification

shared state baseline 已经给出了当前推荐的整组 Web checks，这一组就应该作为 Task 8 的正式自动化回归组合。

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py
