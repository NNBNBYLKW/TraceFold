下面是 **`WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT`** 的正式任务文档初稿。

这一步只处理 **shared state 的视觉与位置一致性**，不处理业务逻辑，不新增 API，不扩成组件平台。依据现有基线，这一轮前端范围仍然只覆盖 **Workbench / Dashboard、Knowledge detail + `knowledge_summary`、Health records + rule alerts**；shared state 需要持续保持 `loading / empty / unavailable / degraded` 的统一语义，同时把 derivation 与 alerts 的局部状态表达得更清楚；并且 Web 仍然**不是** AI center、alerts center、rule console 或 task runtime control center。

---

# `WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT`

## 1. 任务定位

本任务是 **Post-Phase-1 Web 前端优化** 中最克制的一层状态呈现收口任务。

它解决的问题不是“状态定义不清”，而是：

* 状态定义已经基本成立，但不同页面上 **出现的位置、视觉重量、与正文的关系** 仍可能漂移
* 用户在跨页切换时，容易把
  `empty` / `unavailable` / `degraded`、
  `derivation failed` / `formal detail failed`、
  `alerts empty` / `alerts unavailable`
  混成一类
* 当前需要的是 **语义一致的视觉收口**，不是新平台或新框架。

---

## 2. 任务目标

本任务的正式目标是：

> **在不改变既有 shared state 语义、不扩张 Web 能力边界的前提下，统一主消费线页面中 state block 的位置规则、视觉层级和局部降级表达，使用户更容易区分“正在加载”“确实为空”“局部失败”“支持层降级”以及“formal facts 仍可读但 secondary layer 出问题”这几类情况。**

拆开后包含五个子目标：

### 2.1 统一 page-level vs section-level state 的使用边界

要明确：

* **page-level state** 只在 primary read 失败时出现
* **section-level state** 只在 secondary / contextual / local support 出问题时出现

不能再出现“AI 区块失败导致整页像坏掉”“alerts 读失败导致像 formal record 失效”的感觉。Knowledge 与 Health 基线都已明确 secondary layer 失败不能伪装成 formal layer 失败。

### 2.2 统一四类共享状态的主视觉语义

本任务只对齐这四类共享状态的视觉与落点，不重新定义含义：

* `loading`
* `empty`
* `unavailable`
* `degraded`

这些定义在 shared baseline 与 Web consumption baseline 中都已冻结。

### 2.3 统一 derivation 状态的局部表达

Knowledge 页已经明确 derivation 要区分：

* `ready`
* `not generated`
* `failed`
* `invalidated`
* `pending` / `running`
* `unavailable`

且这些都必须与 formal content failure 分开。这个任务要做的是让它们在视觉上更稳定，而不是增加新的 derivation 控制面。

### 2.4 统一 alerts 状态的局部表达

Health 页已经明确 alerts 要区分：

* `open`
* `acknowledged`
* `resolved`
* `empty`
* `unavailable`

并且 `open / acknowledged / resolved` 要保持 lifecycle grouping；`alerts empty` 和 `alerts unavailable` 不能长得像同一回事。

### 2.5 保住“formal facts primary, support layers secondary”

所有状态对齐都必须强化这个事实：

* Knowledge 的 formal content 仍然是 primary
* Health 的 formal records 仍然是 primary
* AI summary 与 alerts 都只是 secondary layer
* Workbench 的 runtime status 是 supporting signal，不是页面主角。

---

## 3. 明确非目标

本任务 **不做**：

* 不改 shared state 语义定义
* 不改后端接口 contract
* 不新增状态种类
* 不新增全局状态管理平台
* 不把 state block 抽成大而全 UI framework
* 不把 Workbench 做成 runtime control center
* 不把 Knowledge 做成 AI center
* 不把 Health 做成 alerts center
* 不新增 Desktop / Telegram 专属状态语义。

这也符合技术路线对界面层的边界要求：界面层负责展示状态、发起操作、降低心智负担，但不负责承载业务真逻辑，更不应发展成各入口各自为政的小系统。

---

## 4. 本任务直接依赖的基线

本任务只应依赖现有已冻结基线：

* `WEB_SHARED_STATE_POLISH_BASELINE.md`
* `WEB_CONSUMPTION_BASELINE.md`
* `WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
* `WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`

其中最关键的现有前提是：

* 本轮页面范围固定为 Workbench / Dashboard、Knowledge、Health
* shared state 语义已经成立
* Knowledge 的 derivation state 已成立
* Health 的 alert state 与 lifecycle grouping 已成立。

---

## 5. 建议输出物

### 5.1 一个最小 shared state visual contract

建议新增一份文档：

`docs/WEB_SHARED_STATE_VISUAL_ALIGNMENT_BASELINE.md`

只记录：

* 哪些状态用 page-level block
* 哪些状态用 section-level block
* 每类状态在视觉上如何区分
* 哪些页面已经完成对齐
* 哪些不在本轮范围内

### 5.2 轻量前端实现收口

建议只做与当前页面真实重复相关的最小实现，例如：

* `StateBlock`
* `InlineStateHint`
* `SectionUnavailableBlock`
* `SectionEmptyBlock`

命名可调，但不要膨胀成新的通用设计系统。

### 5.3 现有测试的局部补强

优先沿用现有 Web 测试与 smoke，不另起一套验证框架。当前基线已明确的推荐检查包括：

* `apps/web/tests/test_semantics_contract.py`
* `apps/web/tests/test_workbench_home_contract.py`
* `apps/web/tests/test_knowledge_ai_ui_contract.py`
* `apps/web/tests/test_health_ai_ui_contract.py`
* `apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py`
* `apps/web/tests/health/test_health_alerts_consumption.py`
* `apps/web/tests/shared/test_shared_state_polish.py`

---

## 6. 具体执行步骤

## Step A：先冻结“状态落点规则”

先写死三条规则：

1. **Primary read failure => page-level unavailable**
2. **Secondary layer failure => section-level unavailable**
3. **Support warning => local degraded, not whole-page collapse**

这是这一步最核心的规则。Workbench baseline 已明确 runtime/supporting degradation 应局部呈现；Knowledge 和 Health 也都要求 secondary layer failure 不得伪装成 primary failure。

## Step B：统一四种共享状态的视觉区分

对齐下面四种状态的视觉语言：

* `loading`
* `empty`
* `unavailable`
* `degraded`

建议只冻结这几个层面的差异：

* 是否占满整个页面还是只占 section
* 是否阻断主阅读流
* 是否需要更强标题提示
* 是否允许正文仍保持可读

关键目标：

* `empty` 不能像错误
* `unavailable` 不能像无数据
* `degraded` 不能像整页失败
* `loading` 不能和空状态混淆。

## Step C：对齐 Knowledge derivation 的局部状态

Knowledge 页应维持以下规则：

* formal detail 失败 => page unavailable
* derivation unavailable => AI section unavailable
* derivation not generated => AI section non-error empty
* derivation failed => AI section failure message，但 formal 继续可读
* invalidated / pending / running => 明确是 derivation lifecycle，而不是正文状态

这里最重要的是：

**不要为了“更显眼”把 derivation 状态做成压过 formal content 的主区块。**

## Step D：对齐 Health alerts 的局部状态

Health 页应维持以下规则：

* formal record 失败 => page unavailable
* alerts unavailable => alerts section unavailable
* alerts empty => alerts section non-error empty
* `open / acknowledged / resolved` => 继续按 lifecycle grouping 展示

这里也要继续保住“alerts 是 derived reminder layer，不是 formal record 替身”。

## Step E：对齐 Workbench runtime/support 状态

Workbench / Dashboard 页应维持：

* 仍然是 entry layer + summary layer
* runtime status 是 supporting signal
* degraded 可以局部挂载
* supporting input 退化时，尽量保留可读 summary，而不是整页 collapse

## Step F：做跨页一致性回看

最后做一轮横向检查，只看这四点：

* 同一个状态词在三页上是否还是同一种意思
* page-level vs section-level 用法是否一致
* formal failure 和 support-layer failure 是否仍然分清
* 是否有某页偷偷把状态块做成新中心

---

## 7. 预期对三条主线的改进

### 7.1 对 Workbench / Dashboard

预期改进：

* 用户更容易看懂“系统警告”与“整页不可用”的区别
* `degraded` 更像支持层提示，而不是主内容失败
* 首页更稳地维持“中控台 / 入口页”角色

### 7.2 对 Knowledge detail

预期改进：

* formal content 与 derivation 状态不会再视觉串位
* `not generated / failed / invalidated / unavailable` 更容易快速识别
* recompute 仍保持小动作，不会因为状态块强化而长成 AI 控制面

### 7.3 对 Health

预期改进：

* alerts empty 和 alerts unavailable 更不容易混淆
* lifecycle grouping 更稳定
* 页面更像“formal record + reminder layer”，而不是 alert console

---

## 8. 验收标准

本任务完成时，应满足以下条件：

1. `loading / empty / unavailable / degraded` 的含义没变，但三页上更容易一眼看懂。
2. primary read failure 与 secondary section failure 的落点明显不同。
3. Knowledge 的 derivation state 不会伪装成 formal detail state。
4. Health 的 alerts empty 与 alerts unavailable 不再像同一状态。
5. Workbench 的 degraded 仍以局部支持块为主，而不是整页 collapse。
6. 没有引入新平台、新中心、新 API 或新的前端业务逻辑中心。
7. 现有相关 Web tests 和 smoke 路径继续通过。

---

## 9. 防偏航检查

出现下面任一趋势，就说明这一步开始偏了：

* 为了“更统一”开始做整站状态框架重写
* 为了“更顺手”把页面状态判断改成前端自持业务逻辑
* 为了“更完整”新增 Web-only 聚合接口
* 为了“更显眼”把 AI 或 alerts 的状态抬成页面主角
* 为了“更有掌控感”把 Workbench 状态做成 runtime control surface
* 为了“适配多入口”长出 Desktop-only / Telegram-only 状态语义

这些方向都和最终软件形态冲突，因为系统要保持的是 **统一工作台、统一主链、统一服务核心、AI 只做派生解释**，而不是让界面层反过来重塑系统中心。

---

## 10. 推荐验证方式

### Manual smoke

* 打开 `/workbench`，确认 `ready / degraded / unavailable` 的相对层级更清楚，且 degraded 不压垮整页。
* 打开任一 knowledge detail，确认 `Formal Content` 仍是主区，AI 区块中的 `not generated / failed / invalidated / unavailable` 更好区分。
* 打开 `/health` 与 detail，确认 `alerts empty` 和 `alerts unavailable` 明显不同，且 lifecycle grouping 仍存在。

### Automated checks

继续优先跑现有组合：

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py