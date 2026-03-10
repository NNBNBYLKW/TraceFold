# 数据模型草案（DATA MODEL DRAFT）

> 本文档用于定义第一阶段的数据模型草案。
> 目标不是一次性定死所有字段，而是先把：
> - 核心实体
> - 主键关系
> - 语义边界
> - 原始层 / 派生层分离
> 明确下来，供后端、前端、Bot、AI 派生层统一参考。

---

## 1. 总体原则

### 1.1 唯一真相源
- SQLite 是唯一真相源
- 原始业务记录必须写入 SQLite
- 图表、导出文件、前端缓存、Markdown、AI 总结都不是主数据源

### 1.2 原始数据与 AI 派生数据分离
- 原始表只存人工确认或明确解析得到的事实
- AI 派生内容必须单独存储
- AI 结果可删除、重算、迭代版本
- AI 结果不得覆盖原始记录

### 1.3 第一阶段优先稳定，不追求极致抽象
- 字段命名优先清晰
- 先保证模块边界正确
- 暂不为“未来可能性”过度抽象

### 1.4 类型与存储约定
- 所有 `*_json` 字段在 SQLite 中统一按 `TEXT` 存储，内容为 JSON 编码字符串
- 所有时间字段统一使用 **UTC**
- 金额字段不使用 `float` 作为业务语义类型；推荐使用：
  - Python 层：`Decimal`
  - SQLite 层：`NUMERIC`
- 枚举值在数据库中统一使用英文代码，前端负责映射中文显示

---

## 2. 核心数据域

本项目第一阶段包含 6 个核心数据域：

1. Capture（统一输入原始层）
2. Pending Review（待确认与修正）
3. Expense（消费）
4. Knowledge（知识）
5. Health（健康）
6. Templates / AI Views（配置与派生层）

---

## 3. ER 关系概览（逻辑层）

```text
Capture
  ├─ 1 -> N PendingReview
  ├─ 1 -> N Expense
  ├─ 1 -> N KnowledgeSource / Knowledge
  ├─ 1 -> N HealthMetric
  └─ 1 -> N HealthSubjectiveRecord

PendingReview
  └─ 1 -> N ReviewAction

Knowledge
  ├─ 1 -> N KnowledgeTag
  └─ N <-> N KnowledgeRelation

HealthMetric
  └─ 1 -> N HealthAlert

Knowledge / Health / Expense / Dashboard
  └─ 1 -> N AIView
```

说明：

- `Capture` 是统一入口原始层
- `PendingReview` 是审核与修正中间层
- `ReviewAction` 用于保留操作历史
- `AIView` 是统一 AI 派生结果表
- `KnowledgeRelation` 第一阶段只保存人工确认关系
- `AIView` 使用多态关联，不做数据库级外键约束，由服务层保证引用合法性

---

## 4. Capture 域

## 4.1 captures

### 作用
统一承接所有输入入口的原始内容，包括：

- Telegram 文本
- Web 表单输入
- 桌面端快速录入
- 后续导入数据

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| module_hint | text | 否 | 入口猜测模块：expense / knowledge / health / unknown |
| source_type | text | 是 | 来源：telegram / web / desktop / import / api / system |
| raw_text | text | 否 | 原始文本输入 |
| raw_payload_json | text (JSON-encoded) | 否 | 原始结构化负载 |
| status | text | 是 | new / parsed / pending / converted / discarded |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 状态说明

- `new`：刚进入系统
- `parsed`：已解析
- `pending`：进入待确认
- `converted`：已转正式记录
- `discarded`：已丢弃

### 约束

- 所有入口应尽量先进入 `captures`
- 不得让不同入口各自绕过 capture 写正式表

---

## 5. Pending Review 域

## 5.1 pending_reviews

### 作用
保存解析结果、候选值、置信度和待确认状态。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| capture_id | integer | 是 | 关联 `captures.id` |
| target_module | text | 是 | expense / knowledge / health |
| parser_result_json | text (JSON-encoded) | 是 | 当前解析结果 |
| candidate_json | text (JSON-encoded) | 否 | 候选建议 |
| confidence_score | float | 否 | 置信度分数 |
| confidence_level | text | 否 | high / medium / low |
| status | text | 是 | open / confirmed / discarded / forced |
| review_notes | text | 否 | 审核备注 |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 状态说明

- `open`：待处理
- `confirmed`：已确认入库
- `discarded`：已丢弃
- `forced`：强制入库

### 约束

- 一个 `capture` 可以关联多个历史 `pending_review`
- 同一时刻，同一个 `capture` 最多只能有一个 `status = open` 的 `pending_review`
- 所有人工确认流程必须通过 `pending_reviews` 状态变化体现

---

## 5.2 review_actions

### 作用
记录每次人工处理行为，便于追溯和后续优化 parser。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| pending_id | integer | 是 | 关联 `pending_reviews.id` |
| action_type | text | 是 | confirm / discard / fix / force_insert |
| before_json | text (JSON-encoded) | 否 | 操作前数据 |
| after_json | text (JSON-encoded) | 否 | 操作后数据 |
| operator_source | text | 否 | 操作来源：web / desktop / telegram / system |
| created_at | datetime | 是 | 创建时间（UTC） |

### 约束

- 所有关键处理动作必须留痕
- 不允许“无记录地修改待确认结果”

---

## 6. Expense 域

## 6.1 expenses

### 作用
保存正式消费记录。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| capture_id | integer | 否 | 来源 capture |
| date | date/datetime | 是 | 消费日期 |
| amount | numeric/decimal | 是 | 金额，业务语义上不使用 float |
| category | text | 是 | 分类 |
| merchant | text | 否 | 商户/对象 |
| note | text | 否 | 补充说明 |
| source_type | text | 是 | 来源：telegram / web / desktop / import / api |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 第一阶段分类建议

建议先控制在较小范围，例如：

- meals
- commute
- daily_necessities
- rent
- entertainment
- travel
- study
- medical
- other

> 前端可映射为：三餐 / 通勤 / 生活用品 / 房租 / 娱乐 / 旅游 / 学习 / 医疗 / 其他

### 说明

- 第一阶段不强做复杂预算模型
- 预算功能可以通过报表/配置层逐步补

---

## 7. Knowledge 域

> 第一阶段采用双层结构：
>
> - 原始层：capture / source
> - 整理层：knowledge card

## 7.1 knowledge_sources（可选，但推荐）

### 作用
保存从 capture 中抽出的知识原始素材，便于后续整理。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| capture_id | integer | 是 | 关联 `captures.id` |
| title | text | 否 | 原始标题 |
| content | text | 是 | 原始内容/摘录 |
| theme | text | 否 | 主题 |
| source_type | text | 否 | article / video / note / book / chat / other |
| source_url | text | 否 | 来源链接 |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 说明

- 如果第一阶段想简化，也可以先不单独建这张表，直接依赖 `captures`
- 但长期来看，保留 `knowledge_sources` 更清楚

---

## 7.2 knowledges

### 作用
保存整理后的知识卡片，是知识模块的核心实体。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| created_from_capture_id | integer | 否 | 来自哪个 capture |
| source_id | integer | 否 | 来自哪个 `knowledge_source` |
| title | text | 是 | 知识标题 |
| summary | text | 否 | 简要摘要 |
| content | text | 是 | 正文/核心内容 |
| theme | text | 否 | 主题 |
| source_type | text | 否 | 来源类型 |
| source_url | text | 否 | 来源链接 |
| status | text | 是 | draft / active / archived |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 状态说明

- `draft`：草稿
- `active`：正式知识卡片
- `archived`：归档

---

## 7.3 knowledge_tags

### 作用
保存知识卡片标签。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| knowledge_id | integer | 是 | 关联 `knowledges.id` |
| tag | text | 是 | 标签名 |

### 约束

- 第一阶段先用简单标签表
- 暂不做 tags 主表、别名表、层级表
- 建议增加唯一约束：`(knowledge_id, tag)`

---

## 7.4 knowledge_relations

### 作用
保存知识卡片之间的人工关系。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| from_knowledge_id | integer | 是 | 起点知识卡片 |
| to_knowledge_id | integer | 是 | 终点知识卡片 |
| relation_type | text | 是 | 关系类型 |
| note | text | 否 | 补充说明 |
| created_at | datetime | 是 | 创建时间（UTC） |

### 第一阶段 relation_type 建议

- related_to
- supports
- derived_from
- contradicts
- example_of

### 关系语义约定

- 第一阶段统一按**有向边**存储
- 即使 `related_to` 或 `contradicts` 在业务上接近无向，数据库中仍保存为单条有向边
- 前端可按需要将某些关系渲染为无向

### 约束

- 第一阶段只存人工确认关系
- 不把自动建议边混入正式边
- 默认不允许自环：`from_knowledge_id != to_knowledge_id`
- 建议增加唯一约束：`(from_knowledge_id, to_knowledge_id, relation_type)`

---

## 8. Health 域

> 第一阶段明确分成两类：
>
> - 硬指标：规则驱动
> - 主观记录：AI 派生分析驱动

## 8.1 health_metrics

### 作用
保存可量化健康指标。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| capture_id | integer | 否 | 来源 capture |
| measurement_group_key | text | 否 | 同一次测量的分组键，用于绑定多条指标（如血压高压/低压） |
| metric_type | text | 是 | heart_rate / sleep_hours / blood_pressure_sys / blood_pressure_dia / weight 等 |
| value_numeric | float | 否 | 数值型指标 |
| value_text | text | 否 | 文本补充值 |
| unit | text | 否 | bpm / hour / kg / mmHg |
| measured_at | datetime | 是 | 测量时间（UTC） |
| source_type | text | 是 | 来源 |
| created_at | datetime | 是 | 创建时间（UTC） |

### 第一阶段 metric_type 建议

- heart_rate
- sleep_hours
- blood_pressure_sys
- blood_pressure_dia
- weight

### 说明

- `measurement_group_key` 主要用于同一次测量的多个指标绑在一起
- 例如一次血压测量可产生两条记录：`blood_pressure_sys` + `blood_pressure_dia`

---

## 8.2 health_subjective_records

### 作用
保存主观感受、恢复情况、不适文本等。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| capture_id | integer | 否 | 来源 capture |
| record_type | text | 是 | symptom / feeling / injury / recovery / medication_note / other |
| title | text | 否 | 简题 |
| content | text | 是 | 原始主观描述 |
| severity_text | text | 否 | 轻/中/重 等文字描述 |
| occurred_at | datetime | 否 | 发生时间（UTC） |
| source_type | text | 是 | 来源 |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 约束

- 原始文本必须保留
- 不以 AI 输出替代原始描述

---

## 8.3 health_alerts

### 作用
保存规则引擎对硬指标触发的提醒结果。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| source_metric_id | integer | 是 | 关联 `health_metrics.id` |
| alert_type | text | 是 | 异常类别 |
| rule_code | text | 是 | 命中的规则编码 |
| severity_level | text | 是 | info / warning / high |
| message | text | 是 | 规则解释文案 |
| status | text | 是 | open / acknowledged / resolved |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 约束

- 第一阶段 alerts 来自显式规则
- 不允许仅由 AI 自行决定 alert 是否成立

---

## 9. Templates 域

## 9.1 templates

### 作用
保存工作模式模板。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| name | text | 是 | 模板名 |
| category | text | 否 | 分类代码 |
| sort_order | integer | 否 | 排序 |
| is_builtin | boolean | 是 | 是否内置 |
| pinned_shortcuts_json | text (JSON-encoded) | 否 | 首页/常用快捷入口配置 |
| query_defaults_json | text (JSON-encoded) | 否 | 查询默认参数 |
| created_at | datetime | 是 | 创建时间（UTC） |
| updated_at | datetime | 是 | 更新时间（UTC） |

### 说明

- 第一阶段模板是配置对象，允许使用 JSON 字段
- 第一阶段不拆成脚本执行模型
- `category` 建议存英文代码，前端映射中文展示

---

## 10. AI 派生层

## 10.1 ai_views

### 作用
统一保存 AI 生成的派生结果。

### 字段建议

| 字段名 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| id | integer | 是 | 主键 |
| module_type | text | 是 | expense / knowledge / health / dashboard |
| target_id | integer | 是 | 对应模块中的目标记录 id |
| view_type | text | 是 | summary / relation_suggestion / trend_explanation / risk_analysis / dashboard_narration |
| content_json | text (JSON-encoded) | 是 | AI 结果内容 |
| model_name | text | 否 | 模型名 |
| version | text | 否 | 版本号 |
| created_at | datetime | 是 | 创建时间（UTC） |

### 说明

- `ai_views` 使用多态关联：
  - `module_type = knowledge` 时，`target_id` 指向 `knowledges.id`
  - `module_type = health` 时，`target_id` 指向健康相关主记录
  - `module_type = expense` 时，`target_id` 指向 `expenses.id`
  - `module_type = dashboard` 时，`target_id` 可用于指向某类聚合对象或约定的虚拟目标
- 第一阶段不做数据库级外键约束，由服务层保证引用合法性
- 第一阶段推荐尽量结构化存储 AI 结果

### `content_json` 建议内容

例如可包含：

- summary
- risk_notes
- suggestions
- confidence_notes
- tag_suggestions

### 约束

- 前端必须标识这是 AI 派生内容
- 可重算、可清理、可替换模型版本

---

## 11. 枚举建议

## 11.1 source_type

建议统一可选值：

- telegram
- web
- desktop
- import
- api
- system

## 11.2 module_type

建议统一可选值：

- expense
- knowledge
- health
- dashboard

## 11.3 pending status

- open
- confirmed
- discarded
- forced

## 11.4 confidence_level

- high
- medium
- low

## 11.5 template category（示例）

建议数据库中使用英文代码：

- full
- expense
- study
- health
- custom

前端可映射显示为：

- 全功能
- 记账
- 学习
- 健康
- 自定义

---

## 12. 第一阶段索引建议

建议尽早加索引的字段：

### captures
- `source_type`
- `status`
- `created_at`

### pending_reviews
- `capture_id`
- `target_module`
- `status`
- `confidence_level`
- `created_at`

### expenses
- `date`
- `category`
- `merchant`

### knowledges
- `theme`
- `status`
- `created_at`
- `updated_at`

### knowledge_tags
- `tag`
- `knowledge_id`

### knowledge_relations
- `from_knowledge_id`
- `to_knowledge_id`
- `relation_type`

### health_metrics
- `metric_type`
- `measured_at`
- `measurement_group_key`

### health_alerts
- `status`
- `severity_level`
- `created_at`

### ai_views
- `module_type`
- `target_id`
- `view_type`
- `created_at`

---

## 13. 第一阶段建议补充的唯一约束

建议尽早加上的唯一约束：

- `knowledge_tags (knowledge_id, tag)`
- `knowledge_relations (from_knowledge_id, to_knowledge_id, relation_type)`

业务级唯一规则（可通过应用层或部分索引实现）：

- 同一个 `capture` 同时最多只有一个 `open` 状态的 `pending_review`

---

## 14. 第一阶段暂不建的表

为防止过度设计，以下表第一阶段暂不建立：

- users
- roles
- permissions
- sync_logs
- obsidian_links
- knowledge_relation_suggestions
- complex_budgets
- recurring_expenses
- full_text_search_aux（可后续再看）
- model_registry（可后续补）

---

## 15. 需要人工最终确认的点

以下内容在正式建表前，建议人工再确认一次：

1. `knowledge_sources` 是否第一阶段就落表
2. `expenses.amount` 最终采用 `Decimal/NUMERIC` 还是最小货币单位整数
3. `templates` 是否坚持 JSON 字段
4. `health_subjective_records.record_type` 的枚举范围
5. `ai_views.content_json` 是否要进一步细分子 schema
6. 是否在第一阶段引入 Alembic
7. `dashboard` 类型的 `ai_views.target_id` 如何约定
8. `knowledge_relations` 中哪些关系前端按无向显示

---

## 16. 推荐给 Codex 的实现顺序

1. 先实现 `captures`
2. 再实现 `pending_reviews` / `review_actions`
3. 再实现 `expenses`
4. 再实现 `knowledges` / `knowledge_tags` / `knowledge_relations`
5. 再实现 `health_metrics` / `health_subjective_records` / `health_alerts`
6. 再实现 `templates`
7. 最后实现 `ai_views`

---

## 17. 完成标准

当以下条件满足时，本草案可进入正式编码阶段：

- [ ] 核心实体边界无明显冲突
- [ ] 表间关系明确
- [ ] 原始层与派生层分离明确
- [ ] Health 的两条处理路径清晰
- [ ] Knowledge 的双层结构清晰
- [ ] 可直接据此生成 SQLAlchemy models 与 Pydantic schemas