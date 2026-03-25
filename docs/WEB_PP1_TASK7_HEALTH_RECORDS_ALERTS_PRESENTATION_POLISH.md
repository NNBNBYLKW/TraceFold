下面是 **`WEB_PP1_TASK7_HEALTH_RECORDS_ALERTS_PRESENTATION_POLISH`** 的正式任务文档初稿。

这一步只处理 **Health 页面展示层优化**，不扩健康 AI，不改 formal facts 权威，不把页面推成 alert center 或 medical assistant UI。当前正式基线已经冻结：Health 页面继续消费 `GET /api/health`、`GET /api/health/{id}`、`GET /api/alerts`，以及最小生命周期动作 `POST /api/alerts/{id}/acknowledge`、`POST /api/alerts/{id}/resolve`；同时页面边界已经明确为 **formal health records 是 primary read layer，rule alerts 是 derived reminder layer**，并且 alert 状态变化 **不等于** 正式健康事实被自动改写。

这一步也必须继续服从 TraceFold 的总边界：项目中心始终是 **个人数据统一工作台**，不是单一健康助手；界面层负责展示状态、发起操作、承载交互、降低心智负担，但不承担业务真逻辑，也不应把某个页面扩成新的产品中心。

---

# `WEB_PP1_TASK7_HEALTH_RECORDS_ALERTS_PRESENTATION_POLISH`

## 1. 任务定位

本任务是 **Post-Phase-1 Web 前端优化** 中的 Health 页面展示收口任务。

它解决的问题不是“Health 页面还没接通”，而是：

- 这条正式消费线已经成立，但还可以进一步强化 **formal records vs rule alerts** 的主次关系
- 当前更重要的是把 Health 页打磨成一个 **formal record read surface + secondary reminder layer**
- 而不是继续把它往 alerts center、rule console 或 medical assistant UI 方向推。

---

## 2. 任务目标

本任务的正式目标是：

> **在不改变当前 Health 页面正式 API 边界与 truth hierarchy 的前提下，优化页面的信息层级、提醒层可读性、lifecycle grouping、状态呈现和轻量动作承载方式，使用户更容易区分 formal health record、source/context、rule alerts 与 alert lifecycle state。**

拆开后包含六个子目标：

### 2.1 强化 `Formal Records` 的主位

Health 页的第一感受，必须先是“我正在看正式健康记录”，而不是“我正在处理提醒”。formal health records 仍然是 primary content，不能被 alerts 区块在视觉上抢位。

### 2.2 强化 `Rule Alerts` 的次位但保留价值

Rule alerts 需要保持清晰可见、确实有用，但必须继续被理解为 **derived from formal health records** 的 secondary reminder layer，而不是正式记录替身。

### 2.3 让 lifecycle grouping 更好读

当前 Health alert section 已区分：

- `open`
- `acknowledged`
- `resolved`
- `empty`
- `unavailable`

并且 `open / acknowledged / resolved` 已按独立子区块呈现，以便看见生命周期而不把页面变成 alert management console。接下来的优化重点，是让这个 grouping 更稳定、更易扫读，而不是改 alert 语义。

### 2.4 让 `alerts empty` 与 `alerts unavailable` 更容易区分

shared state baseline 已经明确：alerts empty 与 alerts unavailable 不是一回事；Health 页也应继续保持这两类状态的明显差异。

### 2.5 让 `Acknowledge Alert` / `Resolve Alert` 保持小动作

这些动作的正式含义已经冻结：它们只是更新 alert lifecycle state、刷新当前页，不打开 workflow 或 task center。这个动作应更顺手，但不能被做成控制台式动作栏。

### 2.6 提高页面整体可读性与低心智负担

最终效果不是“更像提醒系统”，而是让用户更快看懂：

- 哪块是正式健康记录
- 哪块是来源与上下文
- 哪块是 rule alerts
- 当前 alert 处在哪个 lifecycle
- 我是否需要做 acknowledge / resolve

这和界面层“展示状态、发起操作、降低使用心智负担”的职责一致。

---

## 3. 明确非目标

本任务 **不做**：

- 不新增 health AI
- 不新增 medical assistant UI
- 不新增 rule configuration surface
- 不新增 alerts center
- 不改变 alert lifecycle API 语义
- 不让前端承担 alert 业务逻辑
- 不新增 Web-only 聚合 API
- 不顺手重做整个 Health 模块其他页面

这点必须卡死。因为软件最终形态强调的是 **统一底座、清晰边界、长期可维护、低心智负担的个人数据工作台**；健康模块的价值在于提醒、整理、辅助理解，而不是扩成完整医学判断职能或独立提醒产品。

---

## 4. 本任务直接依赖的基线

本任务只依赖当前已冻结前提：

- `WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
- `WEB_CONSUMPTION_BASELINE.md`
- `WEB_SHARED_STATE_POLISH_BASELINE.md`
- `WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
- `WEB_INFORMATION_HIERARCHY_CONTRACT.md`
- `WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH`
- `WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT`

其中最关键的正式边界是：

- Health 页面当前只消费：
  - `GET /api/health`
  - `GET /api/health/{id}`
  - `GET /api/alerts`
  - `POST /api/alerts/{id}/acknowledge`
  - `POST /api/alerts/{id}/resolve`
- 页面必须保持：
  - `Formal Records`
  - `Rule Alerts`
  两个明确层次
- alert state changes 不得被表达成 formal health facts 被自动修正

---

## 5. 建议输出物

### 5.1 一个 Health 页面展示优化实现

建议只做当前页面真正需要的克制收口，例如：

- `HealthDetailHeader`
- `HealthFormalRecordSection`
- `HealthSourceReferenceBlock`
- `HealthRuleAlertsSection`
- `HealthAlertLifecycleGroup`
- `HealthAlertStateBlock`
- `HealthAlertActionRow`

命名可调整，但职责不要膨胀。

### 5.2 一份任务完成文档

建议新增：

`docs/WEB_HEALTH_RECORDS_ALERTS_PRESENTATION_POLISH_BASELINE.md`

记录：

- formal / source / alerts / state / action 的位置关系如何固定
- alert lifecycle grouping 如何呈现
- alerts empty / alerts unavailable 如何区分
- 哪些明确没做

### 5.3 现有测试的局部对齐

当前 Health 相关 Web 验证路径已经存在，正式 baseline 也推荐继续跑：

- `apps/web/tests/test_semantics_contract.py`
- `apps/web/tests/test_health_ai_ui_contract.py`
- `apps/web/tests/health/test_health_alerts_consumption.py`

另外仓库树里也能看到 alerts / health 相关后端测试与模块结构，说明这一步更适合沿用既有契约，而不是另起一套验证体系。

---

## 6. 具体执行步骤

### Step A：先冻结分区顺序

Health detail 的页面顺序建议固定为：

1. 页面标题 / 最小上下文说明
2. `Formal Record`
3. `Source Reference`
4. `Rule Alerts`
5. 局部 alert actions / state

核心判断是：  
**formal record 先于 alerts，source/context 支撑 formal，alerts 再作为提醒层出现。**  
这与当前 Health baseline 和 Web consumption baseline 一致。

### Step B：强化 `Formal Record` 的主阅读流

Formal 区块应优先承载：

- 正式记录内容
- 结构化测量或记录字段
- truth-bearing health detail

它应是页面最容易连续阅读的一段，不应被 alert lifecycle、状态提示或动作区切碎。formal read 失败时，整页才进入 unavailable；不能被 alerts 区块反向干扰。

### Step C：把 `Source Reference` 收成 contextual block

`Source Reference` 要保留，但它的工作是：

- 补充来源
- 支撑理解
- 说明 provenance

而不是：

- 抢主阅读位
- 替代 formal record
- 和 alerts 一起挤成信息墙

### Step D：把 `Rule Alerts` 做成清晰的 secondary section

Alerts 区块建议内部继续保持稳定结构：

1. section title
2. 简短 boundary copy
3. lifecycle grouping
4. ready/readable 时展示：
   - `open`
   - `acknowledged`
   - `resolved`
   - minimal contextual metadata

也就是：它要“清楚、有用、次级”，而不是“弱到看不见”或“强到像页面主角”。

### Step E：对齐 alert states 的视觉与落点

这一页必须继续严格区分：

- formal record unavailable：整页 unavailable
- `alerts empty`：alerts section 的 non-error empty
- `alerts unavailable`：alerts section 的独立 failure
- `open / acknowledged / resolved`：继续作为 lifecycle groups 展示

这里最重要的是：  
**alert 状态永远挂在 alerts section 里，不得伪装成 formal record 状态。**

### Step F：收紧 `Acknowledge Alert` / `Resolve Alert` 的承载方式

这两个动作的建议规则：

- 保留在 alerts section 内
- 作为 small local actions
- 动作文案直接、克制
- 执行后反馈回到当前 lifecycle 语义
- 不额外制造 workflow center 或 incident console 感

### Step G：回看页面是否更像“正式记录页 + 次级提醒层”

最后只检查三个问题：

1. 用户是否先读 formal，再看 alerts
2. 用户是否更容易理解 alerts lifecycle，而不误解 formal truth
3. 页面是否仍然像 TraceFold 统一工作台中的一页，而不是独立提醒产品页

---

## 7. 预期对页面的改进

### 7.1 Formal 与 alerts 的边界更稳

用户会更容易一眼区分：

- 什么是正式健康记录
- 什么是规则派生提醒
- 当前提醒层是不是可用

### 7.2 Lifecycle grouping 更好扫读

`open / acknowledged / resolved / empty / unavailable` 会更不容易混成一类，也更不容易误伤 formal record 的可读感。

### 7.3 小动作更自然，但不会长成 alerts 控制面

这能提高页面顺手度，同时保住“Health 页不是 alerts center”的边界。

---

## 8. 验收标准

本任务完成时，应满足以下条件：

1. 打开任一 Health 页面后，用户更容易先看到 `Formal Record`，再理解 `Source Reference` 和 `Rule Alerts` 的关系。
2. `Formal Records` 明显仍是 primary content，`Rule Alerts` 明显仍是 secondary reminder layer。
3. `open / acknowledged / resolved / empty / unavailable` 更容易区分。
4. formal record failure 与 alerts failure 仍然严格分开。
5. `Acknowledge Alert` 与 `Resolve Alert` 仍是小动作，不是控制台入口。
6. 没有新增 alerts center、rule config UI、medical assistant UI 或 Web-only 聚合 API。
7. 现有 Health 相关 Web tests 继续通过。

---

## 9. 防偏航检查

出现下面任一趋势，就说明这一步开始偏了：

- 为了“更主动”把 alerts 区块抬成页面主角
- 为了“更完整”加入规则配置或医学解释控件
- 为了“更好用”让 alerts 文本覆盖 formal record 主阅读位
- 为了“更集中”把 acknowledge / resolve 做成流程控制面
- 为了“更顺滑”新增 Web-only 聚合接口
- 为了“更统一”把 Health 页变成通用提醒中心模板

这些方向都和 TraceFold 的最终设想冲突，因为项目中心始终是 **个人数据统一工作台**，健康提醒只是克制推进的高价值能力之一，而不是独立产品中心。

---

## 10. 推荐验证方式

### Manual smoke

- 打开 `http://127.0.0.1:3000/health`
- 进入任一 seeded health detail
- 确认页面清楚区分：
  - `Formal Record`
  - `Source Reference`
  - `Rule Alerts`
- 确认 seeded alert 出现在正确 lifecycle heading 下
- 执行 `Acknowledge Alert` 或 `Resolve Alert`
- 确认页面刷新后仍保持 formal 与 alerts 分区

### Automated checks

优先继续跑现有组合：

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py