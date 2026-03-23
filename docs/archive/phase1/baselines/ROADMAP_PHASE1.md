# ROADMAP PHASE 1

## 1. 目标

Phase 1 的目标是建立 TraceFold 的**可扩展 API 基础平台**，而不是一次性做完全部产品形态。

这一阶段的核心任务是：

- 建立稳定的 API 工程骨架
- 建立统一公共层
- 建立 SQLite 数据底座
- 完成少量业务域的最小闭环
- 建立可持续约束 Codex 的文档与任务机制

换句话说，Phase 1 的重点不是“功能很多”，而是：

**先把底座做稳，让后续扩展不需要推翻重来。**

---

## 2. 第一阶段边界

### 2.1 包含内容

Phase 1 包含以下内容：

- API 工程骨架
- 公共配置 / 日志 / 异常 / 响应
- 数据库基础设施
- SQLite 作为唯一真相源
- 基础业务域模型与 CRUD
- 统一任务模板与规则文档
- env 命名与配置规范
- 基础测试与可运行性验证

### 2.2 不包含内容

Phase 1 明确**不包含**以下内容：

- Web 前端正式实现
- Desktop 正式实现
- Bot / Telegram 接入
- Worker 独立进程
- AI 派生层实现
- Obsidian 深整合
- 多用户与权限系统
- 云同步
- 微服务拆分
- 消息队列
- 缓存体系
- 高级插件系统
- 完整搜索系统
- 重型规则引擎
- 医疗诊断式 AI
- 完整移动端 App

这些方向未来可能会做，但**不属于当前 Phase 1 的交付目标**。

---

## 3. 第一阶段总策略

### 3.1 API 先行

当前阶段优先建立 API 底座，而不是同时推进多端。

原因：

- API 是后续 Web / Desktop / Bot 的统一接入层
- API 先稳定，后续接入成本最低
- 更容易通过文档和任务模板约束 Codex

### 3.2 先收口底座，再推进业务域

先建立：

- 统一应用入口与系统级路由
- 统一配置入口
- 统一日志 / 异常 / 响应
- 统一 DB base / session / init

然后再开始落地业务域。

### 3.3 先少量闭环，再继续扩展

Phase 1 不要求五个业务域全部做满。
优先要求：

- 至少 1 个业务域完整跑通
- 最好 2~3 个业务域共用同一套底座
- 新增业务域时不需要重构公共层

### 3.4 文档先行

在当前开发方式下，文档不是附属物，而是实现约束的一部分。

因此第一阶段必须先建立：

- `PROJECT_BRIEF.md`
- `ARCHITECTURE_RULES.md`
- `TASK_TEMPLATE.md`
- `DATA_MODEL_DRAFT.md`
- `ENV_CONVENTIONS.md`
- `ROADMAP_PHASE1.md`

---

## 4. 第一阶段实施原则

### 4.1 先打地基，再做功能

本阶段优先顺序是：

1. 规则文档
2. 应用入口与 API 工程骨架
3. 配置系统
4. 日志 / 异常 / 响应
5. 数据库基础设施
6. 第一个业务域
7. 第二个业务域
8. 第三个业务域
9. 基础测试与收口

### 4.2 不做双轨主架构

旧架构、旧实现、旧试验代码不再作为主线继续扩展。

如果旧代码有参考价值，只能作为：

- 业务参考
- 字段参考
- 流程参考

不能继续作为正式底座延伸。

### 4.3 每完成一个步骤都必须可运行或可验证

不接受：

- 目录看起来完整，但服务起不来
- 文件都有了，但路由没接通
- 配置存在，但主入口没接线
- 表定义了，但数据库无法初始化
- 代码“概念上正确”，但无法验收

Phase 1 强调：

**每一步都必须可验证。**

### 4.4 优先控制复杂度

只要某项能力会显著增加底层复杂度，而当前又不是必须，就应推迟到后续阶段。

### 4.5 不让未完成业务域反向绑架底座

底座阶段允许只导入、只接线、只初始化**当前已真实存在**的模块。
不得为了“看起来完整”而让尚未落地的业务域阻塞公共层、数据库或启动流程。

---

## 5. 第一阶段目标结构

Phase 1 的目标结构聚焦于 API，不展开为完整大仓库多应用实现。

当前目标结构如下：

```text
apps/
└─ api/
   ├─ app/
   │  ├─ main.py
   │  ├─ api/
   │  │  ├─ deps.py
   │  │  ├─ router.py
   │  │  └─ system.py
   │  ├─ core/
   │  │  ├─ config.py
   │  │  ├─ exceptions.py
   │  │  ├─ logging.py
   │  │  └─ responses.py
   │  ├─ db/
   │  │  ├─ base.py
   │  │  ├─ session.py
   │  │  └─ init_db.py
   │  ├─ schemas/
   │  │  └─ common.py
   │  └─ domains/
   │     ├─ capture/
   │     ├─ pending/
   │     ├─ expense/
   │     ├─ knowledge/
   │     └─ health/
   ├─ tests/
   ├─ .env.example
   └─ README.md
docs/
├─ PROJECT_BRIEF.md
├─ ARCHITECTURE_RULES.md
├─ TASK_TEMPLATE.md
├─ DATA_MODEL_DRAFT.md
├─ ENV_CONVENTIONS.md
├─ RISK_DRIFT_WARNINGS.md
└─ ROADMAP_PHASE1.md
```

说明：

- 当前 Phase 1 只把 API 作为正式实现主线
- `knowledge` 与 `health` 可以先预留目录和模型方向
- `RISK_DRIFT_WARNINGS.md` 作为正式约束文档之一保留
- 不要求在本阶段把 Web / Desktop / Bot 一并正式落地

---

## 6. 实施顺序（修订版）

## Step 1：立规则与文档基线

完成：

- `PROJECT_BRIEF.md`
- `ARCHITECTURE_RULES.md`
- `TASK_TEMPLATE.md`
- `DATA_MODEL_DRAFT.md`
- `ENV_CONVENTIONS.md`
- `ROADMAP_PHASE1.md`
- `RISK_DRIFT_WARNINGS.md`（建议一并纳入正式基线）

完成标准：

- 项目边界清晰
- 架构规则清晰
- 数据建模方向清晰
- 风险与偏航预警已成文
- 后续任务可直接套模板发给 Codex

说明：

- Step 1 不以“功能数量”为判断依据
- Step 1 的核心是把项目目标、规则、边界和任务约束立住

---

## Step 2：建立统一应用入口与 API 工程骨架

完成：

- `app/main.py`
- `app/api/router.py`
- `app/api/system.py`
- `app/api/deps.py`
- 目录结构落地
- 基础启动流程打通

完成标准：

- FastAPI 服务可以启动
- 总路由可挂载
- 系统级接口可访问（如 `/api/ping`、`/api/healthz`）
- `main.py` 负责统一 app 创建、异常注册、总路由挂载与 lifespan 入口
- `router.py` 负责总路由装配，不承载过多系统实现细节
- `system.py` 负责系统级接口
- `deps.py` 作为公共依赖入口保留

说明：

- 本步骤只解决“统一入口是否成立”
- 不要求在本步骤同时完成配置、日志、数据库或业务域逻辑

---

## Step 3：建立统一配置系统

完成：

- `app/core/config.py`
- `apps/api/.env.example`
- 与 `ENV_CONVENTIONS.md` 对齐的变量命名与字段定义

完成标准：

- 配置只允许通过 `app/core/config.py` 统一读取
- 变量名统一使用 `TRACEFOLD_API_` 前缀
- `config.py`、`.env.example`、`ENV_CONVENTIONS.md` 三者一致
- 应用入口已真正使用 `settings`
- 不允许业务域直接读取环境变量

说明：

- 本步骤只解决“配置系统是否统一”
- 不以配置项多少为完成标准，而以“统一入口 + 一致命名 + 已接线”为标准

---

## Step 4：建立统一日志、异常与响应层

完成：

- `app/core/logging.py`
- `app/core/exceptions.py`
- `app/core/responses.py`

完成标准：

- 日志可统一初始化，并由应用入口接线
- 全局异常处理器已注册
- 成功与失败响应结构可统一复用
- 错误返回不再由各域各自拼装
- 公共响应结构已可被后续业务域直接使用

说明：

- 本步骤只解决运行时公共层是否成立
- 不要求在本步骤实现完整日志策略或复杂异常分层

---

## Step 5：建立数据库基础设施

完成：

- `app/db/base.py`
- `app/db/session.py`
- `app/db/init_db.py`

完成标准：

- SQLite 可连接
- 统一 Base 可用
- 统一 Session 可用
- 建表流程可运行
- 启动阶段可触发最小数据库初始化
- 无业务域私建 DB 基础设施

说明：

- 允许 `init_db.py` 只导入当前已真实存在的模型模块
- 不要求为了“显得完整”而提前导入尚未实现的所有业务域

---

## Step 6：实现第一个业务域（capture）

完成：

- `domains/capture/models.py`
- `domains/capture/schemas.py`
- `domains/capture/repository.py`
- `domains/capture/service.py`
- `domains/capture/router.py`
- 在 `app/api/router.py` 中注册 capture 路由
- 在 `app/db/init_db.py` 中补充 capture 模型导入

完成标准：

- 可创建记录
- 可查询记录
- 可读取单条记录
- 可更新记录
- 可删除记录
- 使用统一 DB session
- 使用统一响应结构
- 事务仍由 `service` 控制
- router 轻薄，不在路由层堆事务或复杂逻辑

---

## Step 7：实现第二个业务域（pending）

完成：

- `domains/pending/models.py`
- `domains/pending/schemas.py`
- `domains/pending/repository.py`
- `domains/pending/service.py`
- `domains/pending/router.py`
- 在 `app/api/router.py` 中注册 pending 路由
- 在 `app/db/init_db.py` 中补充 pending 模型导入

建议字段：

- `status`
- `priority`
- `due_at`（可选）

完成标准：

- pending 基础接口可用
- capture 与 pending 可同时工作
- 未出现重复公共能力实现
- 未破坏依赖方向规则
- 不抢跑提醒调度、复杂状态机或树形子任务

---

## Step 8：实现第三个业务域（expense）

完成：

- `domains/expense/models.py`
- `domains/expense/schemas.py`
- `domains/expense/repository.py`
- `domains/expense/service.py`
- `domains/expense/router.py`
- 在 `app/api/router.py` 中注册 expense 路由
- 在 `app/db/init_db.py` 中补充 expense 模型导入

建议字段：

- `amount_minor`
- `currency`
- `category`
- `spent_at`

完成标准：

- expense 基础接口可用
- capture / pending / expense 共用同一套底座
- 金额字段按约定正确落地
- 架构未发生破坏性调整
- 不顺手加入预算、统计、搜索、导出等超范围能力

---

## Step 9：基础测试、文档对齐与阶段收口

完成：

- 基础测试目录建立
- 至少覆盖核心服务启动
- 至少覆盖 1~3 个业务域的基础链路
- 文档与代码对齐检查
- 配置、路由、数据库初始化、公共层实际接线复核

完成标准：

- 基础测试可运行
- 核心 API 链路可验证
- 配置文档与代码一致
- 路由注册、公共层接线、数据库初始化三条链路已实际验证
- 无明显架构漂移
- 当前阶段的实现状态可以被清楚复盘与继续扩展

说明：

- 本步骤的重点是“收口”和“对齐”
- 不在本步骤追加新大功能
- 如确有异步需求，只允许建立最小占位与边界说明，不得引入独立 worker、消息队列或复杂任务平台

---

## 7. 第一阶段建议优先级

建议优先级如下：

### P0（必须完成）

- 文档规则基线
- 统一应用入口与 API 工程骨架
- 统一配置系统
- 日志 / 异常 / 响应公共层
- DB 基础设施
- capture 基础 CRUD

### P1（强烈建议完成）

- pending 基础 CRUD
- expense 基础 CRUD
- 基础测试
- 配置规范与示例文件同步
- 文档与代码收口检查

### P2（可以后置）

- knowledge 基础域实现
- health 基础域实现
- 更细的异常分类
- 更完整的测试覆盖
- 最小后台任务承载点的正式化实现

---

## 8. 第一阶段交付结果

Phase 1 结束时，至少应交付：

### 8.1 文档层

- 项目说明文档
- 架构规则文档
- 任务模板
- 数据模型草案
- env 规范
- 风险与偏航预警文档
- 阶段路线图

### 8.2 工程层

- FastAPI 服务可启动
- SQLite 接入可运行
- 公共层已成型
- 总路由与系统级接口可用
- 配置统一由正式入口读取

### 8.3 业务层

至少完成以下之一：

- `capture` 完整 CRUD

更理想的目标是完成：

- `capture`
- `pending`
- `expense`

这 3 个业务域的基础 CRUD。

### 8.4 质量层

- 基础测试可运行
- 配置读取统一
- 无业务域私建 DB / config / logging
- 文档与代码大体一致
- 各步骤均有可复查的完成依据

---

## 9. 完成判定

若满足以下条件，则可视为 Phase 1 基础达成：

- API 项目结构清晰且稳定
- 统一应用入口已成立
- 公共层已成型
- 所有配置通过统一入口读取
- SQLite 作为唯一真相源工作正常
- 所有业务域复用统一 DB base / session
- 至少 1~3 个业务域接入成功
- 新增一个业务域不需要重构公共底座
- Codex 可在既定规则下继续实现后续任务

---

## 10. 风险提醒

### 风险 1：业务域偷长底座

表现：

- 各域自己读取 env
- 各域自己建 session
- 各域自己写返回格式
- 各域复制日志与异常处理

解决：

- 严格执行 `ARCHITECTURE_RULES.md`
- 后续增加检查脚本
- 所有任务强制使用 `TASK_TEMPLATE.md`

### 风险 2：任务描述太模糊

表现：

- Codex 自由发挥
- 目录结构漂移
- 跨层写逻辑
- 提前引入不需要的复杂度

解决：

- 每个任务都写清：
  - Goal
  - Scope
  - Architecture constraints
  - Config constraints
  - Deliverables
  - Acceptance criteria

### 风险 3：过早追求多端与高级能力

表现：

- 还没把基础链路跑通就去做 Web
- 还没把公共层立稳就去接 Bot
- 还没把数据底座跑通就去做 AI 派生层

解决：

- 严格遵守本路线图顺序
- 非 P0 / P1 项目默认后置
- 先稳 API，再谈多端

### 风险 4：文档与代码脱节

表现：

- 规则文档一套，代码实现另一套
- env 文档未同步
- 数据模型草案和 ORM 实现不一致

解决：

- 每次重大改动先更文档再改代码
- 每个阶段结束后做一次文档对齐检查

### 风险 5：用“看起来完整”替代“真正接线”

表现：

- 文件创建了，但主入口没有调用
- `.env.example` 有了，但配置系统没真正读取
- `init_db.py` 有了，但启动流程不执行
- 路由文件存在，但总路由未挂载

解决：

- 每一步都要求实际接线验证
- 不以文件数量，而以可运行性与可验证性为完成标准

---

## 11. 当前阶段最重要的判断

TraceFold 在当前阶段最重要的，不是尽快做出很多功能页面，而是先确认：

- API 底座是否稳定
- 统一应用入口是否成立
- 公共层是否成型
- SQLite 是否真正成为统一真相源
- 新增业务域是否还能保持结构不乱
- Codex 是否已经被文档成功约束住

只要这几件事成立，Phase 1 就是成功的。

---

## 12. 本文档的定位

本文档不负责描述全部未来规划，而是定义：

- 第一阶段到底先做什么
- 第一阶段明确不做什么
- 开发顺序怎么排
- 每一步完成到什么程度才算有效
- 在细化路线与总纲之间，哪些内容应以本路线图为正式执行顺序基线

它是 Phase 1 的**执行顺序文档**，用于约束“先后顺序”和“阶段边界”，而不是用于堆产品想象。
