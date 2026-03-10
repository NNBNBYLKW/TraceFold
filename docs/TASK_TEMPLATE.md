# TASK TEMPLATE

> 本文档用于规范后续交给 Codex 的实现任务格式。
> 目标：减少跑偏，提高一次完成率，确保实现持续符合 TraceFold Phase 1 的架构约束。

---

## 1. 使用原则

每次给 Codex 下任务时，尽量使用本模板，并把关键信息写完整。

每个任务至少要明确：

1. 任务目标
2. 修改范围
3. 非范围
4. 架构约束
5. 配置约束
6. 交付清单
7. 验收标准
8. 汇报格式

如果任务描述不完整，Codex 很容易出现以下问题：

- 修改范围失控
- 越层写逻辑
- 偷加配置项
- 复制公共能力
- 提前实现不在当前阶段范围内的复杂能力

---

## 2. 总体约束

所有任务默认都必须遵守以下原则：

1. 当前 Phase 1 主线是 **API 底座建设**
2. 当前主架构是 **业务域 + 公共层**
3. SQLite 是唯一真相源
4. 配置必须通过统一入口读取
5. 事务默认由 `service` 控制
6. 路由必须统一注册到 `app/api/router.py`
7. 不允许业务域私自创建 DB 基础设施
8. 不允许私自引入当前阶段未批准的复杂能力

如某个任务与以下文档冲突，默认以这些文档为准：

- `docs/PROJECT_BRIEF.md`
- `docs/ARCHITECTURE_RULES.md`
- `docs/DATA_MODEL_DRAFT.md`
- `docs/ENV_CONVENTIONS.md`
- `docs/ROADMAP_PHASE1.md`

---

## 3. 通用任务模板

```md
# Task: <任务标题>

## Goal
<清楚描述这次任务要实现什么。>
<不要写空泛目标，要写可交付结果。>

## Scope
允许修改：
- <文件或目录 1>
- <文件或目录 2>
- <文件或目录 3>

允许新增：
- <文件或目录 1>
- <文件或目录 2>

## Out of scope
禁止修改：
- <文件或目录 1>
- <文件或目录 2>

不要做：
- <不在本任务范围内的事项 1>
- <不在本任务范围内的事项 2>
- <顺手扩展的高复杂度事项 3>

## Project context
当前项目背景：
- TraceFold Phase 1 以 API 底座建设为主
- 架构采用“业务域 + 公共层”
- SQLite 是唯一真相源
- 所有配置统一由 `app/core/config.py` 管理
- 事务默认由 `service` 控制
- 路由统一在 `app/api/router.py` 注册
- 详见：
  - `docs/PROJECT_BRIEF.md`
  - `docs/ARCHITECTURE_RULES.md`
  - `docs/DATA_MODEL_DRAFT.md`
  - `docs/ENV_CONVENTIONS.md`

## Files to read first
开始前必须先阅读：
- `docs/PROJECT_BRIEF.md`
- `docs/ARCHITECTURE_RULES.md`
- `docs/DATA_MODEL_DRAFT.md`
- `docs/ENV_CONVENTIONS.md`
- <与本任务直接相关的现有文件>

## Architecture constraints
必须遵守：
1. 业务代码只能放在 `apps/api/app/domains/<domain>/`
2. 不允许在业务域直接读取 env
3. 不允许新建 DB engine / session / Base
4. router 中不允许直接写复杂业务逻辑
5. router 中不允许直接写复杂 ORM / SQL 操作
6. 事务默认由 `service` 控制
7. 路由必须统一注册到 `apps/api/app/api/router.py`
8. 不允许复制公共能力到业务域
9. 不允许新增未批准的顶层架构模式
10. 不允许新增无边界的 `utils.py` / `helpers.py` / `misc.py`

## Config constraints
如果新增、删除、重命名配置项，必须同步修改：
- `apps/api/app/core/config.py`
- `apps/api/.env.example`
- `docs/ENV_CONVENTIONS.md`

如果本任务不涉及配置变更，请明确写：
- 本任务默认不新增 env 配置项

## Data / API constraints
如涉及模型或接口，请明确写清楚：
- 表名
- 字段
- 字段含义
- 是否可空
- 状态枚举
- API 路由
- 请求 schema
- 响应 schema

## Deliverables
至少交付：
- <文件 1>
- <文件 2>
- <文件 3>

如果涉及注册或接线，也要写清：
- 在 `app/api/router.py` 中完成路由注册
- 在 `db/init_db.py` 中补充必要模型导入
- 在测试目录中新增对应测试文件

## Acceptance criteria
完成标准：
- [ ] 代码位于正确目录
- [ ] 未违反架构规则
- [ ] 路由已正确注册
- [ ] 使用统一 DB session / Base
- [ ] 使用统一响应结构
- [ ] 未出现重复公共能力实现
- [ ] 如有配置变更，文档已同步
- [ ] 基础测试通过
- [ ] 本任务范围内功能可运行

## Output format
完成后请按以下格式汇报：

1. 修改了哪些文件
2. 每个文件做了什么
3. 哪些点已完成
4. 哪些点未完成或被刻意留空
5. 是否有需要我手动确认的地方
6. 本地运行 / 测试命令
7. 是否有潜在风险或后续建议
```

---

## 4. 编写任务时的补充要求

### 4.1 Goal 要写成“可验收目标”

不要写：

- 优化一下 capture
- 完善 pending
- 搭一个大概能用的版本

要写：

- 实现 capture 域基础 CRUD
- 为 pending 域增加 status / priority / due_at 的基础接口
- 为 expense 域增加创建、列表、详情、更新、删除接口

---

### 4.2 Scope 一定要限制修改范围

Codex 容易“顺手多改”。

所以每个任务都要明确：

- 哪些文件可以改
- 哪些文件不能改
- 哪些文件可以新增
- 哪些公共层文件禁止碰

---

### 4.3 Out of scope 要显式阻止扩张

尤其要防止以下常见跑偏：

- 顺手加前端
- 顺手加认证
- 顺手加搜索
- 顺手加 AI
- 顺手改公共层结构
- 顺手引入新依赖
- 顺手实现另一个业务域

---

### 4.4 涉及配置时必须显式说明

任何和 env 有关的任务，都要写明：

- 是否允许新增配置项
- 新增哪些配置项
- 必须同步哪些文件

不允许让 Codex自己判断后偷偷加 env。

---

### 4.5 验收标准必须可检查

不要写：

- 看起来合理
- 结构更清晰
- 基本可用

要写：

- 路由能在 OpenAPI 中看到
- 服务可启动
- 测试通过
- 创建后能查到记录
- 使用统一 DB session
- 未新增第二套 Base

---

## 5. 推荐任务粒度

Phase 1 推荐任务保持“小而明确”。

推荐粒度：

- 一个公共层文件
- 一个系统接线任务
- 一个业务域基础 CRUD
- 一个测试补全任务

不推荐一次下达过大的复合任务，例如：

- 一次实现 5 个业务域
- 一次同时改架构、改配置、改数据库、改前端
- 一次把 API / Web / Desktop / Bot 一起做掉

原则：

**一个任务最好只有一个清晰主目标。**

---

## 6. 示例任务 1：实现 capture 域基础 CRUD

```md
# Task: Implement basic CRUD for capture domain

## Goal
实现 capture 域的基础 CRUD API，用于保存和管理简短记录条目。

## Scope
允许修改：
- `apps/api/app/domains/capture/*`
- `apps/api/app/api/router.py`
- `apps/api/app/db/init_db.py`
- `apps/api/tests/domains/capture/*`

允许新增：
- `apps/api/app/domains/capture/router.py`
- `apps/api/app/domains/capture/service.py`
- `apps/api/app/domains/capture/repository.py`
- `apps/api/app/domains/capture/models.py`
- `apps/api/app/domains/capture/schemas.py`

## Out of scope
禁止修改：
- `apps/api/app/db/session.py`
- `apps/api/app/db/base.py`
- `apps/api/app/core/config.py`（除非任务明确需要配置变更）
- `apps/api/app/core/logging.py`
- `apps/api/app/core/exceptions.py`

不要做：
- 不要实现 knowledge 域逻辑
- 不要引入认证
- 不要加入搜索功能
- 不要新增 AI 处理逻辑
- 不要新增与 capture 无关的公共抽象

## Project context
当前项目背景：
- TraceFold Phase 1 以 API 底座建设为主
- 架构采用“业务域 + 公共层”
- SQLite 是唯一真相源
- 所有配置统一由 `app/core/config.py` 管理
- 事务默认由 `service` 控制
- 路由统一在 `app/api/router.py` 注册

## Files to read first
开始前必须先阅读：
- `docs/PROJECT_BRIEF.md`
- `docs/ARCHITECTURE_RULES.md`
- `docs/DATA_MODEL_DRAFT.md`
- `docs/ENV_CONVENTIONS.md`
- `apps/api/app/api/router.py`
- `apps/api/app/db/init_db.py`

## Architecture constraints
必须遵守：
1. capture 业务代码只能放在 `apps/api/app/domains/capture/`
2. 不允许在 capture 域直接读取环境变量
3. 不允许创建新的 DB engine / session / Base
4. router 中不允许直接写复杂 ORM / SQL 逻辑
5. router 中不允许直接写复杂业务规则
6. 事务默认由 `service` 控制
7. capture 路由必须统一注册到 `apps/api/app/api/router.py`
8. 不允许复制公共响应或异常处理逻辑

## Config constraints
- 本任务默认不新增 env 配置项

## Data / API constraints
表名建议：
- `capture_items`

字段：
- `id`
- `content`
- `source`
- `created_at`
- `updated_at`

接口：
- `POST /capture`
- `GET /capture`
- `GET /capture/{id}`
- `PUT /capture/{id}`
- `DELETE /capture/{id}`

说明：
- `content` 为必填文本
- `source` 为必填文本，Phase 1 可支持如 `manual` / `bot` / `import` / `api`

## Deliverables
至少交付：
- `apps/api/app/domains/capture/router.py`
- `apps/api/app/domains/capture/service.py`
- `apps/api/app/domains/capture/repository.py`
- `apps/api/app/domains/capture/models.py`
- `apps/api/app/domains/capture/schemas.py`
- `apps/api/app/api/router.py` 中完成注册
- `apps/api/app/db/init_db.py` 中补充必要模型导入
- `apps/api/tests/domains/capture/` 下对应测试文件

## Acceptance criteria
- [ ] create/list/get/update/delete 基础接口可用
- [ ] 路由已注册
- [ ] 使用统一 DB session
- [ ] 使用统一响应结构
- [ ] repository 未直接 commit
- [ ] 事务由 service 控制
- [ ] 未出现重复公共能力实现
- [ ] 基础测试通过

## Output format
完成后请按以下格式汇报：

1. 修改了哪些文件
2. 每个文件做了什么
3. 哪些点已完成
4. 哪些点未完成或被刻意留空
5. 是否有需要我手动确认的地方
6. 本地运行 / 测试命令
7. 是否有潜在风险或后续建议
```

---

## 7. 示例任务 2：实现 pending 域基础 CRUD

```md
# Task: Implement basic CRUD for pending domain

## Goal
实现 pending 域的基础 CRUD API，用于保存待处理事项，并支持基础状态与优先级字段。

## Scope
允许修改：
- `apps/api/app/domains/pending/*`
- `apps/api/app/api/router.py`
- `apps/api/app/db/init_db.py`
- `apps/api/tests/domains/pending/*`

允许新增：
- `apps/api/app/domains/pending/router.py`
- `apps/api/app/domains/pending/service.py`
- `apps/api/app/domains/pending/repository.py`
- `apps/api/app/domains/pending/models.py`
- `apps/api/app/domains/pending/schemas.py`

## Out of scope
禁止修改：
- `apps/api/app/db/session.py`
- `apps/api/app/db/base.py`
- `apps/api/app/core/config.py`（除非任务明确需要配置变更）

不要做：
- 不要实现提醒调度系统
- 不要实现子任务树
- 不要实现复杂状态机
- 不要接入其他业务域逻辑
- 不要引入搜索或 AI

## Project context
当前项目背景：
- TraceFold Phase 1 以 API 底座建设为主
- 架构采用“业务域 + 公共层”
- SQLite 是唯一真相源
- 所有配置统一由 `app/core/config.py` 管理
- 事务默认由 `service` 控制
- 路由统一在 `app/api/router.py` 注册

## Files to read first
开始前必须先阅读：
- `docs/PROJECT_BRIEF.md`
- `docs/ARCHITECTURE_RULES.md`
- `docs/DATA_MODEL_DRAFT.md`
- `docs/ENV_CONVENTIONS.md`

## Architecture constraints
必须遵守：
1. pending 业务代码只能放在 `apps/api/app/domains/pending/`
2. 不允许在业务域直接读取 env
3. 不允许新建 DB engine / session / Base
4. router 必须轻薄
5. repository 默认不直接 commit
6. 事务由 service 控制
7. 路由统一注册到 `apps/api/app/api/router.py`

## Config constraints
- 本任务默认不新增 env 配置项

## Data / API constraints
表名建议：
- `pending_items`

字段：
- `id`
- `title`
- `description`
- `status`
- `priority`
- `due_at`
- `created_at`
- `updated_at`

状态建议：
- `open`
- `in_progress`
- `done`
- `cancelled`

接口：
- `POST /pending`
- `GET /pending`
- `GET /pending/{id}`
- `PUT /pending/{id}`
- `DELETE /pending/{id}`

## Deliverables
至少交付：
- `apps/api/app/domains/pending/router.py`
- `apps/api/app/domains/pending/service.py`
- `apps/api/app/domains/pending/repository.py`
- `apps/api/app/domains/pending/models.py`
- `apps/api/app/domains/pending/schemas.py`
- `apps/api/app/api/router.py` 中完成注册
- `apps/api/app/db/init_db.py` 中补充必要模型导入
- `apps/api/tests/domains/pending/` 下对应测试文件

## Acceptance criteria
- [ ] 基础 CRUD 可用
- [ ] status / priority / due_at 已落地
- [ ] 使用统一 DB session
- [ ] repository 未直接 commit
- [ ] 事务由 service 控制
- [ ] 路由已注册
- [ ] 基础测试通过
```

---

## 8. 示例任务 3：实现 expense 域基础 CRUD

```md
# Task: Implement basic CRUD for expense domain

## Goal
实现 expense 域的基础 CRUD API，用于记录个人消费，并按 Phase 1 约定存储金额与货币字段。

## Scope
允许修改：
- `apps/api/app/domains/expense/*`
- `apps/api/app/api/router.py`
- `apps/api/app/db/init_db.py`
- `apps/api/tests/domains/expense/*`

允许新增：
- `apps/api/app/domains/expense/router.py`
- `apps/api/app/domains/expense/service.py`
- `apps/api/app/domains/expense/repository.py`
- `apps/api/app/domains/expense/models.py`
- `apps/api/app/domains/expense/schemas.py`

## Out of scope
禁止修改：
- `apps/api/app/db/session.py`
- `apps/api/app/db/base.py`
- `apps/api/app/core/config.py`（除非任务明确需要配置变更）

不要做：
- 不要实现预算系统
- 不要实现汇率服务
- 不要实现多账户记账
- 不要引入发票附件索引
- 不要顺手扩展其他业务域

## Project context
当前项目背景：
- TraceFold Phase 1 以 API 底座建设为主
- 架构采用“业务域 + 公共层”
- SQLite 是唯一真相源
- 所有配置统一由 `app/core/config.py` 管理
- 事务默认由 `service` 控制
- 路由统一在 `app/api/router.py` 注册

## Files to read first
开始前必须先阅读：
- `docs/PROJECT_BRIEF.md`
- `docs/ARCHITECTURE_RULES.md`
- `docs/DATA_MODEL_DRAFT.md`
- `docs/ENV_CONVENTIONS.md`

## Architecture constraints
必须遵守：
1. expense 业务代码只能放在 `apps/api/app/domains/expense/`
2. 不允许在业务域直接读取 env
3. 不允许新建 DB engine / session / Base
4. router 中不允许写复杂业务逻辑
5. repository 默认不直接 commit
6. 事务由 service 控制
7. 路由统一注册到 `apps/api/app/api/router.py`

## Config constraints
- 本任务默认不新增 env 配置项

## Data / API constraints
表名建议：
- `expense_items`

字段：
- `id`
- `amount_minor`
- `currency`
- `category`
- `note`
- `spent_at`
- `created_at`
- `updated_at`

说明：
- `amount_minor` 使用最小货币单位整数
- `currency` 示例：`GBP` / `CNY` / `USD`

接口：
- `POST /expense`
- `GET /expense`
- `GET /expense/{id}`
- `PUT /expense/{id}`
- `DELETE /expense/{id}`

## Deliverables
至少交付：
- `apps/api/app/domains/expense/router.py`
- `apps/api/app/domains/expense/service.py`
- `apps/api/app/domains/expense/repository.py`
- `apps/api/app/domains/expense/models.py`
- `apps/api/app/domains/expense/schemas.py`
- `apps/api/app/api/router.py` 中完成注册
- `apps/api/app/db/init_db.py` 中补充必要模型导入
- `apps/api/tests/domains/expense/` 下对应测试文件

## Acceptance criteria
- [ ] 基础 CRUD 可用
- [ ] amount_minor / currency / spent_at 已正确落地
- [ ] 使用统一 DB session
- [ ] repository 未直接 commit
- [ ] 事务由 service 控制
- [ ] 路由已注册
- [ ] 基础测试通过
```

---

## 9. 不推荐的任务写法

以下写法不推荐直接发给 Codex：

### 9.1 目标过大

例如：

- 把整个 TraceFold 第一阶段都做完
- 先把所有模块都搭起来
- 先把 API、桌面端、规则引擎一起做了

问题：

- 范围失控
- 很难验收
- 很容易架构漂移

---

### 9.2 约束过少

例如：

- 帮我把 pending 写一下
- 做个 capture 模块
- 自己看着实现

问题：

- Codex 会自由发挥
- 很容易偷偷改公共层
- 很容易引入不在当前阶段的设计

---

### 9.3 Scope 和 Out of scope 不清

例如只写“实现 expense”，不写禁止事项。

问题：

- 可能顺手加预算、统计、搜索、标签、导出
- 最后交付物远超当前任务需要

---

## 10. 本文档的定位

本文档不是实现代码，而是用于定义：

- 以后如何给 Codex 下任务
- 什么样的任务描述才足够清晰
- 如何限制任务范围
- 如何避免 Codex 跑偏
- 如何让每个任务都可验收、可回顾、可持续迭代

在 TraceFold Phase 1 中，任务模板本身就是工程约束的一部分。