下面是 **`WEB_PP1_TASK5_WORKBENCH_DASHBOARD_HIERARCHY_POLISH`** 的正式任务文档初稿。

这一步只处理 **Workbench / Dashboard 的页面层级与首屏信息主次**，不扩功能边界，不改后端主线。依据当前正式 Web 基线，Workbench / Dashboard 现在正式消费 `GET /api/workbench/home`、`GET /api/dashboard`、`GET /api/system/status`；并且其角色已经冻结为：**Workbench 是 entry layer，不是第二业务中心；Dashboard 是 summary layer，不是 formal domain pages 的替代品；runtime status 可以局部降级而不压垮整页**。  
同时，最终软件设想明确强调：这个系统需要的是 **首页工作台 / 中控台体验**，重点是“进入正确上下文”，而不是把首页做成单页功能最强的控制台。

---

# `WEB_PP1_TASK5_WORKBENCH_DASHBOARD_HIERARCHY_POLISH`

## 1. 任务定位

本任务是 **Post-Phase-1 Web 前端优化** 中的首页与总览层级优化任务。

它解决的问题不是“Workbench / Dashboard 还没接通”，而是：

* 这条正式消费线已经成立，但首页仍可能存在 **信息平铺、主次不够清楚、首屏不够像工作台入口** 的问题
* 当前更重要的是把首页打磨成 **进入上下文、判断当前状态、决定下一步去向** 的页面
* 而不是继续把 Workbench / Dashboard 往“大而全控制台”方向推。

---

## 2. 任务目标

本任务的正式目标是：

> **在不新增产品中心、不改动当前正式 API 消费边界的前提下，优化 Workbench / Dashboard 的首页层级、首屏信息组织、状态挂载方式与入口主次关系，使其更像 TraceFold 的工作台入口与总结层，而不是一个平铺信息墙或控制台页面。**

拆开后包含五个子目标：

### 2.1 强化首屏“现在怎样、接下来去哪”的可读性

首页第一屏应优先回答：

* 系统当前总体情况怎样
* 有哪些当前值得看的内容
* 接下来最适合进入哪个正式页面或工作上下文

这与最终需求里“首页工作台与模板化工作模式”“中控台体验优先于单页功能强大”的方向一致。

### 2.2 稳定 Workbench 与 Dashboard 的角色区分

当前角色已冻结：

* Workbench = entry layer
* Dashboard = summary layer

所以这一步要做的是 **强化角色差异**，不是把二者重新揉成一个“超级首页”。

### 2.3 让 runtime/support 状态局部化

当前 shared state 基线已经明确：

* Workbench / Dashboard 使用 shared runtime status panel 表达 `ready / degraded / unavailable`
* supporting inputs 退化时，应尽量保留可读 summary
* workbench summary failure 应优先作为 local degraded block，而不是整页 collapse。

### 2.4 提高首页信息密度的可扫描性

这里不是单纯“加卡片”或“压缩留白”，而是让用户更快区分：

* 主摘要
* 支持摘要
* 状态提示
* 后续入口
* 上下文说明

### 2.5 保住“工作台入口”而不是“第二业务中心”

技术路线和软件最终需求都强调：界面层是业务的可视化外壳，不是业务真逻辑中心；首页需要的是总览与切换中心，而不是独立控制面。

---

## 3. 明确非目标

本任务 **不做**：

* 不新增新的首页聚合 API
* 不把 Workbench 做成第二业务中心
* 不把 Dashboard 做成 formal domain page 的替代物
* 不把 runtime status 做成 runtime control center
* 不加入 AI center / alerts center / task center 入口
* 不扩成首页模板系统重做
* 不借首页优化之名改动 Knowledge / Health 的正式页面边界

这点必须卡死。因为当前 Web 已明确 **不是** AI center、alerts center、rule console、task runtime control center；如果首页为了“更完整”开始聚拢这些中心能力，就已经偏航。

---

## 4. 本任务直接依赖的基线

本任务只依赖当前已冻结前提：

* `WEB_CONSUMPTION_BASELINE.md`
* `WEB_SHARED_STATE_POLISH_BASELINE.md`
* `WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
* `WEB_INFORMATION_HIERARCHY_CONTRACT.md`
* `WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH`
* `WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT`

其中最关键的已知正式边界是：

* Workbench / Dashboard 消费：
  * `GET /api/workbench/home`
  * `GET /api/dashboard`
  * `GET /api/system/status`
* Workbench 仍是 entry layer
* Dashboard 仍是 summary layer
* runtime status 可局部降级

---

## 5. 建议输出物

### 5.1 首页层级优化实现

建议只做当前页面所需的克制收口，例如：

* `WorkbenchPageHeader`
* `WorkbenchPrimarySummarySection`
* `WorkbenchNextStepEntrySection`
* `WorkbenchSupportingSummarySection`
* `RuntimeStatusSupportBlock`

命名可调整，但职责不要膨胀。

### 5.2 一份任务完成文档

建议新增：

`docs/WEB_WORKBENCH_DASHBOARD_HIERARCHY_POLISH_BASELINE.md`

记录：

* 首屏层级如何调整
* Workbench 与 Dashboard 的关系如何呈现
* runtime/support 状态如何局部挂载
* 哪些明显没做

### 5.3 现有测试的局部对齐

优先沿用现有 Web tests，不另起体系。当前已存在与 Workbench 相关的测试与代码结构，包括 workbench domain 代码和 workbench tests。`tree.txt` 中可见 `workbench` 模块及测试如 `test_workbench_api.py`、`test_workbench_recent_recorder.py`。

---

## 6. 具体执行步骤

## Step A：先冻结首屏优先级

先明确首页首屏只容纳四类信息，且必须按顺序组织：

1. **当前整体状态**
2. **当前最值得看的摘要**
3. **下一步进入点**
4. **支持性说明 / 状态**

不要首屏平铺多个同权卡片。

## Step B：明确 Workbench 与 Dashboard 的视觉关系

建议规则：

* Workbench 承担“现在系统怎样、去哪里”的主叙事
* Dashboard 承担“当前摘要信息”的支持叙事
* Dashboard 不应压过 Workbench 的入口功能
* Workbench 不应膨胀成所有域信息平铺容器

## Step C：局部挂载 runtime status

共享状态规则已经明确：

* `degraded` 尽量局部化
* supporting input 失败尽量不压垮整页
* 只有 primary read 真失败时才用 page-level unavailable

因此首页应做到：

* runtime warning 可见
* 但主摘要仍尽量可读
* supporting summary failure 不伪装成首页整体失效

## Step D：整理首页动作主次

首页允许有导航或继续工作入口，但动作必须服务于“进入正确上下文”，而不是做成控制台按钮墙。

建议：

* 强动作只保留少量高价值进入点
* 其余入口降为 supporting navigation
* 不把首页做成 task / AI / alerts 操作集中区

## Step E：压缩“解释性噪音”

Workbench / Dashboard 需要 contextual support，但这些区块必须后置：

* runtime explanation
* freshness / status hints
* lower-priority supporting notes

它们应帮助理解，不应抢占首屏主位。

## Step F：回看是否更像“工作台”

最后只看三个问题：

* 用户打开后，是否更快知道当前系统怎样
* 是否更快知道下一步去哪
* 是否更少把首页当成“另一个业务页面”

---

## 7. 预期对页面的改进

### 7.1 首页首屏更像“中控台 / 入口页”

软件最终需求明确把首页定义为 **总入口 + 中控台 + 审核台 + 长期归档底座** 体验的一部分，因此首页应优先承担“进入工作状态”的职责。

### 7.2 Dashboard 更像 summary layer，而不是并列中心

它仍应提供摘要，但不应与正式 domain pages 争中心位。

### 7.3 degraded 更容易被理解为局部支持性退化

这与 shared state baseline 保持一致：warning-level condition 存在时，内容仍可部分可读。

---

## 8. 验收标准

本任务完成时，应满足以下条件：

1. 打开 `/workbench` 后，用户更容易在首屏回答：
   * 当前系统怎样
   * 当前该看什么
   * 下一步去哪

2. Workbench 明显仍是 entry layer，而不是第二业务中心。

3. Dashboard 明显仍是 summary layer，而不是 formal domain pages 的替代物。

4. `degraded` 和 supporting failure 主要以局部块呈现，不轻易整页 collapse。

5. 没有新增首页专属业务逻辑中心，也没有新增 Web-only 聚合 API。界面层仍只是展示状态、发起操作、降低心智负担，而非业务真逻辑中心。

6. 现有 workbench 相关测试与 smoke 继续通过。`tree.txt` 已显示 workbench tests 和 runtime/system tests 存在。

---

## 9. 防偏航检查

出现下面任一趋势，就说明这一步开始偏了：

* 为了“首页更强”开始新增大量首页专属 API
* 把首页做成多域控制台
* 把 runtime status 做成控制中心
* 把各类入口、摘要、状态都做成同权大卡片墙
* 为了“中控台感”把首页抬成 formal pages 的替代物
* 因为 Desktop / Telegram 差异，开始长出入口专属首页语义

这些方向都和最终软件设想冲突。你要的是 **统一工作台入口**，不是新的业务中心；你要的是 **统一主链与统一服务中心**，不是让首页反向承载业务真逻辑。

---

## 10. 推荐验证方式

### Manual smoke

* 打开 `http://127.0.0.1:3000/workbench`
* 确认首屏更清楚地区分：
  * workbench home summary
  * dashboard summary
  * runtime status
  * next-step entry
* 确认 `ready / degraded / unavailable` 的位置更清楚，且 degraded 不压垮整页

### Automated checks

优先继续跑现有 Web checks，至少包括：

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py