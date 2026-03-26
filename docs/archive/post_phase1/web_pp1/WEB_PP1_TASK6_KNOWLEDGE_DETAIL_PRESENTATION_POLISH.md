下面是 **`WEB_PP1_TASK6_KNOWLEDGE_DETAIL_PRESENTATION_POLISH`** 的正式任务文档初稿。

这一步只处理 **Knowledge detail 的展示层优化**，不扩 AI 能力边界，不改正式数据权威，不新增 Web-only 聚合 API。当前正式基线已经冻结：Knowledge detail 继续消费 `GET /api/knowledge/{id}`、`GET /api/ai-derivations/knowledge/{id}` 与 `POST /api/ai-derivations/knowledge/{id}/recompute`；页面保持两个明确分区：`Formal Content` 与 `AI-derived Summary`；formal content 仍是 truth-bearing primary section，AI summary 只是 secondary interpretation，`Recompute AI-derived Summary` 只是一个小动作，不是 AI 控制台入口。 

同时，这一步必须继续服从 TraceFold 的总边界：界面层负责展示状态、发起操作、承载交互、呈现 AI 与正式数据的区别、降低心智负担；AI 作为派生解释层存在，不能污染 formal facts，也不能让前端页面滑向 AI center。  

---

# `WEB_PP1_TASK6_KNOWLEDGE_DETAIL_PRESENTATION_POLISH`

## 1. 任务定位

本任务是 **Post-Phase-1 Web 前端优化** 中的 Knowledge 详情页展示收口任务。

它解决的问题不是“Knowledge detail 还没接通”，而是：

* 这条页面链路已经正式成立，但还可以进一步强化 **formal vs AI** 的主次关系
* 当前更重要的是把 Knowledge detail 打磨成一个 **formal record read page + derived reading aid**
* 而不是继续把它往 AI center、模型控制面板、prompt 面板或任务中心方向推。 

---

## 2. 任务目标

本任务的正式目标是：

> **在不改变当前 Knowledge detail 的正式 API 边界与 truth hierarchy 的前提下，优化页面的信息层级、分区可读性、状态呈现和轻量动作承载方式，使用户更容易区分 formal content、source/context、AI-derived summary 与 derivation state。**

拆开后包含六个子目标：

### 2.1 强化 `Formal Content` 的主位

Knowledge detail 的第一页感受，必须先是“我正在看一条正式知识记录”，而不是“我正在看模型总结结果”。formal content 仍然是 primary section，不能被 AI 区块视觉抢位。 

### 2.2 强化 `AI-derived Summary` 的次位但保留价值

AI-derived summary 需要保持清晰可见、确实有帮助，但必须继续被理解为 **generated from the formal record** 的 secondary interpretation，而不是事实权威或页面中心。 

### 2.3 让 derivation states 更好辨认

当前已成立的 derivation states 包括：

* `ready`
* `not generated`
* `failed`
* `invalidated`
* `pending` / `running`
* `unavailable`

这一步要做的是让这些状态在视觉与位置上更清楚，而不是新增状态种类。  

### 2.4 让 `Source Reference` 更像 contextual support

`Source Reference` 应帮助用户理解 formal record 的来源和上下文，但不能在视觉上与 formal content 或 AI section 争主位。Knowledge 页应仍然是“正式记录优先，其次是来源与 AI 派生支撑”。

### 2.5 让 `Recompute AI-derived Summary` 保持小动作

Recompute 的正式含义已经很窄：请求对 formal `knowledge_summary` 再生成，不修改 formal record，不把页面变成 task center。这个动作应更顺手，但不能被做成主按钮墙或 AI 操作台。

### 2.6 提高页面整体可读性与低心智负担

这一步的最终效果，不是更“炫”，而是让用户更快看懂：

* 哪块是正式内容
* 哪块是来源支撑
* 哪块是 AI 派生
* 当前 AI 状态是什么
* 我是否需要点 recompute

这符合界面层“降低使用心智负担”的职责。

---

## 3. 明确非目标

本任务 **不做**：

* 不新增 AI provider / model / prompt controls
* 不新增 AI center 或 task center
* 不改变 `knowledge_summary` 的正式 derivation 边界
* 不让前端承担 derivation 业务逻辑
* 不新增 Web-only 聚合 API
* 不让 AI 区块取得 formal truth authority
* 不顺手重做整个 Knowledge 模块列表页。  

这点必须卡死。因为 TraceFold 的最终形态始终是“统一底座、清晰边界、长期可维护、低心智负担的个人数据工作台”，不是强 AI 自动化平台，也不是让模型替代人工掌握正式数据。 

---

## 4. 本任务直接依赖的基线

本任务只依赖当前已冻结前提：

* `WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
* `WEB_CONSUMPTION_BASELINE.md`
* `WEB_SHARED_STATE_POLISH_BASELINE.md`
* `WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
* `WEB_INFORMATION_HIERARCHY_CONTRACT.md`
* `WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH`
* `WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT`

其中最关键的正式边界是：

* Knowledge detail 当前只消费：

  * `GET /api/knowledge/{id}`
  * `GET /api/ai-derivations/knowledge/{id}`
  * `POST /api/ai-derivations/knowledge/{id}/recompute`

* 页面必须保持：

  * `Formal Content`
  * `AI-derived Summary`

  两个明确分区

* formal detail failure 与 derivation failure 必须分开处理。 

---

## 5. 建议输出物

### 5.1 一个 Knowledge detail 展示优化实现

建议只做当前 detail 页真正需要的克制收口，例如：

* `KnowledgeDetailHeader`
* `KnowledgeFormalContentSection`
* `KnowledgeSourceReferenceBlock`
* `KnowledgeAiSummarySection`
* `KnowledgeDerivationStateBlock`
* `KnowledgeAiActionRow`

命名可调整，但职责不要膨胀。

### 5.2 一份任务完成文档

建议新增：

`docs/WEB_KNOWLEDGE_DETAIL_PRESENTATION_POLISH_BASELINE.md`

记录：

* 页面分区怎样收口
* formal / source / AI / state / action 的位置关系如何固定
* derivation states 如何呈现
* 哪些明显没做

### 5.3 现有测试的局部对齐

当前仓库里已经有与 Knowledge 相关的 Web tests：

* `apps/web/tests/test_knowledge_ai_ui_contract.py`
* `apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py`
* 以及 shared 语义相关测试。仓库树里也能看到 `knowledge` 领域与 `ai_summary.py`、`service.py`、`router.py` 等现有结构，因此本轮更适合沿用既有契约而不是再开新验证体系。  

---

## 6. 具体执行步骤

## Step A：先冻结分区顺序

Knowledge detail 的页面顺序建议固定为：

1. 页面标题 / 最小上下文说明
2. `Formal Content`
3. `Source Reference`
4. `AI-derived Summary`
5. 局部 derivation action / status

其中核心判断是：  
**formal content 先于 AI，source/context 支撑 formal，AI 再作为派生阅读辅助出现。**

这与当前基线保持一致，也符合“前端展示必须区分正式数据与 AI 派生内容”的技术路线。 

## Step B：强化 `Formal Content` 的主阅读流

Formal 区块应优先承载：

* 标题
* 主体正文 / 正式字段
* truth-bearing structured content

它应是页面最容易连续阅读的一段，不应被 derivation metadata、状态提示、按钮条切碎。formal read 失败时，整页才进入 unavailable；这点不能被 AI 区块反向干扰。

## Step C：把 `Source Reference` 收成 contextual block

Source Reference 要保留，但它的工作是：

* 补充来源
* 支撑理解
* 说明 provenance

而不是：

* 抢主阅读位
* 替代 formal content
* 和 AI 区块一起挤成信息墙。

## Step D：把 `AI-derived Summary` 做成清晰的 secondary section

AI 区块建议内部继续保持稳定结构：

1. section title
2. 简短 boundary copy
3. derivation state block
4. ready 时展示：

   * `summary`
   * `key_points`
   * `keywords`
   * minimal derivation metadata

也就是：它要“清楚、有用、次级”，而不是“弱到看不见”或“强到像页面主角”。当前正式输出字段也只接受 `summary / key_points / keywords`。

## Step E：对齐 derivation states 的视觉与落点

这一页必须继续严格区分：

* `ready`：展示 summary / key points / keywords
* `not generated`：AI section 的 non-error empty
* `failed`：AI section 的 failure message，但 formal 继续可读
* `invalidated`：旧派生结果已过时，应重算
* `pending` / `running`：重算进行中
* `unavailable`：AI derivation read 失败，但不代表 formal detail 失败。  

这里最重要的是：  
**derivation state 永远挂在 AI section 里，不得伪装成整页状态。**

## Step F：收紧 `Recompute AI-derived Summary` 的承载方式

Recompute 的建议规则：

* 保留在 AI section 内
* 作为 small local action
* 动作文案直接、克制
* 点击后反馈回到当前 section 语义
* 不额外制造“任务中心感”或“AI控制台感”。

## Step G：回看页面是否更像“正式记录页 + 派生阅读辅助”

最后只检查三个问题：

1. 用户是否先读 formal，再看 AI
2. 用户是否更容易理解 AI 的状态而不误解 formal truth
3. 页面是否仍然像 TraceFold 的统一工作台中的一页，而不是独立 AI 产品页。 

---

## 7. 预期对页面的改进

### 7.1 Formal 与 AI 的边界更稳

用户会更容易一眼区分：

* 什么是正式知识记录
* 什么是模型派生解释
* 当前 AI 层是不是可用

这正符合现有 Knowledge baseline 的核心目的。 

### 7.2 Derivation states 更好扫读

`not generated / failed / invalidated / pending / unavailable` 会更不容易混成一类，也更不容易误伤 formal detail 的可读感。 

### 7.3 Recompute 更自然，但不会长成 AI 控制面

这能提高页面顺手度，同时保住“Knowledge detail 不是 AI center”的边界。 

---

## 8. 验收标准

本任务完成时，应满足以下条件：

1. 打开任一 knowledge detail 后，用户更容易先看到 `Formal Content`，再理解 `Source Reference` 和 `AI-derived Summary` 的关系。
2. `Formal Content` 明显仍是 primary section，AI summary 明显仍是 secondary section。 
3. `ready / not generated / failed / invalidated / pending / unavailable` 更容易区分。
4. formal detail failure 与 derivation failure 仍然严格分开。
5. `Recompute AI-derived Summary` 仍是小动作，不是控制台入口。
6. 没有新增 AI center、prompt/model control UI、task center 或 Web-only 聚合 API。
7. 现有 Knowledge 相关 Web tests 继续通过。 

---

## 9. 防偏航检查

出现下面任一趋势，就说明这一步开始偏了：

* 为了“更智能”把 AI 区块抬成页面主角
* 为了“更好用”加入 provider / model / prompt controls
* 为了“更顺滑”让 AI 文本覆盖 formal content 的主阅读位
* 为了“更集中”把 recompute 做成任务中心入口
* 为了“更完整”新增 Web-only 聚合接口
* 为了“更统一”把 Knowledge detail 变成通用 AI 详情页模板

这些方向都和 TraceFold 的最终设想冲突，因为项目中心始终是 **个人数据统一工作台**，AI 只做派生解释与展示增强，不是正式事实的替代权威。 

---

## 10. 推荐验证方式

### Manual smoke

* 打开 `http://127.0.0.1:3000/knowledge`
* 进入任一 seeded knowledge record
* 确认页面清楚区分：

  * `Formal Content`
  * `Source Reference`
  * `AI-derived Summary`

* 确认 ready 时展示 `summary / key_points / keywords`
* 触发 `Recompute AI-derived Summary`
* 确认页面刷新后仍保持 formal 与 AI 分区。  

### Automated checks

优先继续跑现有组合：

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/shared/test_shared_state_polish.py