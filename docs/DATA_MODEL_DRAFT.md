# DATA MODEL DRAFT

## 1. 目标

本文档定义 TraceFold Phase 1 的数据模型草案。

目标不是一次性把所有表设计到最终形态，而是先建立一套：

- 可落地
- 可维护
- 可扩展
- 不容易让 Codex 跑偏

的数据建模基线。

---

## 2. 建模前提

Phase 1 的数据模型基于以下前提：

1. **SQLite 是唯一真相源**
   - 第一阶段所有结构化数据统一进入 SQLite
   - 不允许各业务域私自维护额外数据库作为真相源

2. **当前仅服务单用户**
   - Phase 1 不引入 `user_id`
   - 不提前为多租户设计增加复杂度

3. **先少表，后细分**
   - 优先保证主流程可用
   - 暂不为了未来可能性过度正规化

4. **先稳定基础字段，再演进关系结构**
   - Phase 1 优先主表
   - tags、attachments、relations、AI extraction 等能力可后续拆分

5. **按业务域组织表**
   - 表结构归属于对应业务域
   - 公共层不承载业务表定义

---

## 3. 全局建模约定

### 3.1 主键约定

Phase 1 默认所有主表使用：

- `id`：`INTEGER` 主键，自增

原因：

- 与 SQLite 配合最简单
- 本地开发和调试成本最低
- 对 Codex 最容易约束实现

后续若需要跨端同步或外部公开标识符，可在 Phase 2 再补充：
- `public_id`
- `uuid`

但 Phase 1 不强制引入。

---

### 3.2 时间字段约定

默认时间字段统一使用 UTC。

推荐通用字段：

- `created_at`
- `updated_at`

按业务需要增加：

- `due_at`
- `spent_at`
- `recorded_at`
- `occurred_at`
- `completed_at`

原则：

- 所有时间统一按 UTC 存储
- 展示层再根据客户端时区转换
- 不在数据库层混用本地时区

---

### 3.3 通用字段约定

大多数业务表建议至少包含：

- `id`
- `created_at`
- `updated_at`

可按域需要追加：

- `status`
- `note`
- `source`

Phase 1 默认**不统一引入**以下字段，除非某域确实需要：

- `deleted_at`
- `archived_at`
- `version`
- `user_id`

---

### 3.4 命名约定

字段命名统一使用：

- 小写
- snake_case
- 单数语义
- 避免缩写不清

例如：

- `created_at`
- `updated_at`
- `record_type`
- `amount_minor`

避免：

- `crt_time`
- `upd`
- `amt`
- `val1`

---

### 3.5 金额约定

消费金额默认不使用浮点数。

推荐字段：

- `amount_minor`：以最小货币单位存储的整数
- `currency`：货币代码，如 `GBP`、`CNY`

示例：

- `£12.34` -> `amount_minor = 1234`, `currency = "GBP"`

原因：

- 避免浮点误差
- SQLite 下更稳定
- 后续统计和导出更容易控制

---

### 3.6 tags 约定

Phase 1 中，若某域需要 tags：

- 默认先使用简单文本字段存储
- 不急于拆成 `tag` / `tag_map` 关系表

例如：

- `tags_text`：可存逗号分隔、JSON 字符串或约定格式文本

注意：

- Phase 1 只要求“可用”
- 不要求一开始就做到完全正规化

---

## 4. Phase 1 域模型草案

## 4.1 capture

### 4.1.1 目标

`capture` 用于快速记录碎片化输入，是整个系统最轻量、最先落地的业务域之一。

典型场景：

- 快速记一句话
- 从 Bot / 导入脚本写入一条记录
- 临时收集待整理内容

### 4.1.2 建议字段

- `id`
- `content`
- `source`
- `created_at`
- `updated_at`

### 4.1.3 字段说明

- `content`
  - 类型建议：`TEXT`
  - 必填
  - 存放记录正文

- `source`
  - 类型建议：`TEXT`
  - 必填
  - 表示来源，如：
    - `manual`
    - `bot`
    - `import`
    - `api`

- `created_at`
  - 类型建议：UTC datetime
  - 必填

- `updated_at`
  - 类型建议：UTC datetime
  - 必填

### 4.1.4 Phase 1 不做

- 不做复杂分类体系
- 不做附件表
- 不做 NLP 抽取结果表
- 不做多层状态流转

---

## 4.2 pending

### 4.2.1 目标

`pending` 用于管理待处理事项、待整理项目或下一步动作。

典型场景：

- TODO
- 稍后处理
- 待分类
- 待回顾

### 4.2.2 建议字段

- `id`
- `title`
- `description`
- `status`
- `priority`
- `due_at`
- `created_at`
- `updated_at`

### 4.2.3 字段说明

- `title`
  - 类型建议：`TEXT`
  - 必填
  - 简要标题

- `description`
  - 类型建议：`TEXT`
  - 可空
  - 详细说明

- `status`
  - 类型建议：`TEXT`
  - 必填
  - Phase 1 建议值：
    - `open`
    - `in_progress`
    - `done`
    - `cancelled`

- `priority`
  - 类型建议：`INTEGER`
  - 可空
  - Phase 1 可约定：
    - `1` = low
    - `2` = medium
    - `3` = high

- `due_at`
  - 类型建议：UTC datetime
  - 可空

- `created_at`
  - 必填

- `updated_at`
  - 必填

### 4.2.4 Phase 1 不做

- 不做复杂任务依赖图
- 不做子任务树
- 不做提醒调度系统
- 不做多人协作状态

---

## 4.3 expense

### 4.3.1 目标

`expense` 用于记录个人消费或支出流水。

典型场景：

- 日常消费记录
- 分类统计
- 事后复盘
- 导出分析

### 4.3.2 建议字段

- `id`
- `amount_minor`
- `currency`
- `category`
- `note`
- `spent_at`
- `created_at`
- `updated_at`

### 4.3.3 字段说明

- `amount_minor`
  - 类型建议：`INTEGER`
  - 必填
  - 使用最小货币单位存储金额

- `currency`
  - 类型建议：`TEXT`
  - 必填
  - 示例：
    - `GBP`
    - `CNY`
    - `USD`

- `category`
  - 类型建议：`TEXT`
  - 可空
  - 示例：
    - `food`
    - `transport`
    - `shopping`
    - `subscription`

- `note`
  - 类型建议：`TEXT`
  - 可空
  - 自由备注

- `spent_at`
  - 类型建议：UTC datetime
  - 必填
  - 表示实际消费发生时间

- `created_at`
  - 必填

- `updated_at`
  - 必填

### 4.3.4 Phase 1 不做

- 不做预算系统
- 不做多账户复式记账
- 不做汇率换算历史
- 不做发票/附件索引表

---

## 4.4 knowledge

### 4.4.1 目标

`knowledge` 用于承载知识条目、笔记索引或整理后的内容。

典型场景：

- 保存整理后的笔记
- 存放知识摘要
- 建立可后续扩展的知识记录底座

### 4.4.2 建议字段

- `id`
- `title`
- `content`
- `source`
- `tags_text`
- `created_at`
- `updated_at`

### 4.4.3 字段说明

- `title`
  - 类型建议：`TEXT`
  - 必填

- `content`
  - 类型建议：`TEXT`
  - 必填

- `source`
  - 类型建议：`TEXT`
  - 可空
  - 用于记录来源，如：
    - `manual`
    - `capture`
    - `import`
    - `web`

- `tags_text`
  - 类型建议：`TEXT`
  - 可空
  - Phase 1 先简单存储 tags 文本
  - 后续若搜索、过滤、统计需求明显，再拆表

- `created_at`
  - 必填

- `updated_at`
  - 必填

### 4.4.4 Phase 1 不做

- 不做知识图谱关系表
- 不做附件表
- 不做块级编辑模型
- 不做全文检索优化表

---

## 4.5 health

### 4.5.1 目标

`health` 用于保存个人健康相关记录。

第一阶段先采用**单表通用模型**，先覆盖不同类型记录的落地能力。
后续再根据实际使用频率拆分专表或补充规则系统。

### 4.5.2 设计原则

健康域在你的整体规划里，后续会同时覆盖：

- 硬指标记录
  - 如 sleep、heart_rate、weight、steps
- 主观记录
  - 如 mood、fatigue、feeling、note

因此第一阶段不急着拆成多张专表，而是先保证：

- 能统一录入
- 能统一查询
- 能为后续规则触发预留空间

### 4.5.3 建议字段

- `id`
- `record_type`
- `value_text`
- `value_number`
- `unit`
- `note`
- `recorded_at`
- `created_at`
- `updated_at`

### 4.5.4 字段说明

- `record_type`
  - 类型建议：`TEXT`
  - 必填
  - 示例：
    - `sleep`
    - `heart_rate`
    - `weight`
    - `steps`
    - `mood`
    - `fatigue`

- `value_text`
  - 类型建议：`TEXT`
  - 可空
  - 用于主观描述或文本型值

- `value_number`
  - 类型建议：数值类型
  - 可空
  - 用于硬指标数值

- `unit`
  - 类型建议：`TEXT`
  - 可空
  - 示例：
    - `bpm`
    - `kg`
    - `hours`
    - `steps`

- `note`
  - 类型建议：`TEXT`
  - 可空
  - 补充描述

- `recorded_at`
  - 类型建议：UTC datetime
  - 必填
  - 表示实际记录时间

- `created_at`
  - 必填

- `updated_at`
  - 必填

### 4.5.5 Phase 1 不做

- 不做复杂医学结构化模型
- 不做健康规则引擎完整实现
- 不做设备同步适配层
- 不做多张专表拆分（如 `sleep_records`、`heart_rate_records` 等）

### 4.5.6 后续演进方向

当以下需求变强时，可考虑第二阶段拆分：

- sleep 需要更多结构字段
- 心率需要区分静息 / 运动 / 峰值
- 需要规则触发与阈值提醒
- 需要设备导入映射

届时可以考虑从通用表演进到：

- 专表
- 规则表
- 导入映射表

但这不属于 Phase 1 范围。

---

## 5. 跨域关系策略

Phase 1 默认尽量避免复杂跨域外键关系。

原则：

1. 先保证各域独立可用
2. 不为了“理论上更完整”而过早增加强耦合
3. 若需要跨域关联，优先在 service 层编排，而不是立刻引入复杂关系网

示例：

- `capture` 的内容未来可能被整理进 `knowledge`
- `pending` 未来可能来源于 `capture`
- `expense` 未来可能附带 `capture` 来源说明

这些关系在 Phase 1 中可以先通过：
- 业务流程编排
- 文本引用
- 简单字段追踪

而不是一开始就设计大量关系表。

---

## 6. Phase 1 建模约束

### 原则 1：不为未来过度设计

如果当前没有明确需求，不要提前加入：

- 多租户字段
- 大量索引
- 复杂状态机字段
- 抽象过度的父类表

### 原则 2：同一类含义尽量统一命名

例如：

- 时间统一用 `*_at`
- 备注优先用 `note`
- 来源优先用 `source`

避免同义混用：

- `remark` / `memo` / `comment`
- `time` / `date_time` / `created_time`

### 原则 3：让 Codex 更容易正确实现

字段设计要优先满足：

- 清晰
- 稳定
- 易于 CRUD
- 易于测试

而不是追求数据库教科书式完美。

### 原则 4：表数量控制

Phase 1 应优先控制表数量，先把以下主表落地：

- `capture_items`
- `pending_items`
- `expense_items`

`knowledge` 和 `health` 可在基础底座稳定后继续接入。

---

## 7. 第一阶段优先实现顺序建议

建议按以下顺序落地：

1. `capture`
2. `pending`
3. `expense`
4. `knowledge`
5. `health`

原因：

- `capture` 最简单，最适合验证全链路
- `pending` 能验证状态字段和基础业务规则
- `expense` 能验证金额与时间处理
- `knowledge` 与 `health` 更适合在底座稳定后接入

---

## 8. 建议表名方向

Phase 1 可采用明确、稳定、便于理解的表名：

- `capture_items`
- `pending_items`
- `expense_items`
- `knowledge_items`
- `health_records`

说明：

- 不强制要求所有表都叫 `xxx_items`
- 但命名应统一、稳定、可读
- `health_records` 比 `health_items` 语义更自然

---

## 9. 暂缓决策项

以下问题在 Phase 1 可以先不做复杂决策：

1. `knowledge.tags_text` 最终采用什么编码形式
2. `health` 是否拆成多张专表
3. 是否引入软删除字段 `deleted_at`
4. 是否增加 `public_id` / `uuid`
5. 是否增加附件索引表
6. 是否增加关系映射表

这些都应遵循一个原则：

**先出现明确需求，再做结构升级。**

---

## 10. 给 Codex 的实现提醒

后续让 Codex 根据本文档生成模型时，应额外约束：

1. 不要擅自新增多余字段
2. 不要提前实现 Phase 2 的复杂关系
3. 不要把业务规则塞进 ORM model
4. 不要在模型层读 env
5. 不要在业务域自行创建第二套数据库设施

---

## 11. 本文档的定位

本文档是 Phase 1 的数据模型草案，不是最终数据库白皮书。

它的作用是：

- 给 Codex 一个稳定的建模边界
- 给后续 CRUD 实现提供统一方向
- 控制第一阶段复杂度
- 防止在还没跑通主流程前就过度设计