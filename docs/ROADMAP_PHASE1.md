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

### 3.2 先公共层，再业务域

先建立：

- config
- logging
- exceptions
- responses
- db base / session / init
- api router

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
2. API 工程骨架
3. 公共层
4. 数据库基础设施
5. 第一个业务域
6. 第二个业务域
7. 第三个业务域
8. 基础测试与收口

### 4.2 不做双轨主架构

旧架构、旧实现、旧试验代码不再作为主线继续扩展。

如果旧代码有参考价值，只能作为：

- 业务参考
- 字段参考
- 流程参考

不能继续作为正式底座延伸。

### 4.3 每完成一个步骤都必须可运行

不接受：

- 目录看起来完整，但服务起不来
- 文件都有了，但路由没接通
- 表定义了，但数据库无法初始化
- 代码“概念上正确”，但无法验收

Phase 1 强调：

**每一步都必须可验证。**

### 4.4 优先控制复杂度

只要某项能力会显著增加底层复杂度，而当前又不是必须，就应推迟到后续阶段。

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
└─ ROADMAP_PHASE1.md
```

说明：

- 当前 Phase 1 只把 API 作为正式实现主线
- `knowledge` 与 `health` 可以先预留目录和模型方向
- 不要求在本阶段把 Web / Desktop / Bot 一并正式落地

---

## 6. 实施顺序

## Step 1：立规则与文档基线

完成：

- `PROJECT_BRIEF.md`
- `ARCHITECTURE_RULES.md`
- `TASK_TEMPLATE.md`
- `DATA_MODEL_DRAFT.md`
- `ENV_CONVENTIONS.md`
- `ROADMAP_PHASE1.md`

完成标准：

- 项目边界清晰
- 架构规则清晰
- 数据建模方向清晰
- 后续任务可直接套模板发给 Codex

---

## Step 2：建立 API 工程骨架

完成：

- `app/main.py`
- `app/api/router.py`
- `app/api/deps.py`
- `app/api/system.py`
- 目录结构落地
- 基础启动流程打通

完成标准：

- FastAPI 服务可以启动
- 总路由可挂载
- 系统级接口可访问（如 `/ping` / `/healthz`）

---

## Step 3：建立公共层

完成：

- `core/config.py`
- `core/logging.py`
- `core/exceptions.py`
- `core/responses.py`

完成标准：

- 配置可统一读取
- 日志可统一初始化
- 异常可统一处理
- 基础响应格式可统一复用

---

## Step 4：建立数据库基础设施

完成：

- `db/base.py`
- `db/session.py`
- `db/init_db.py`

完成标准：

- SQLite 可连接
- 统一 Base 可用
- 统一 Session 可用
- 建表流程可运行
- 无业务域私建 DB 基础设施

---

## Step 5：实现第一个业务域（capture）

完成：

- `domains/capture/models.py`
- `domains/capture/schemas.py`
- `domains/capture/repository.py`
- `domains/capture/service.py`
- `domains/capture/router.py`
- 在 `app/api/router.py` 中注册 capture 路由

完成标准：

- 可创建记录
- 可查询记录
- 可读取单条记录
- 可更新记录
- 可删除记录
- 使用统一 DB session
- 使用统一响应结构

---

## Step 6：实现第二个业务域（pending）

完成：

- pending 域基础 CRUD
- `status`
- `priority`
- `due_at`（可选）

完成标准：

- pending 基础接口可用
- capture 与 pending 可同时工作
- 未出现重复公共能力实现
- 未破坏依赖方向规则

---

## Step 7：实现第三个业务域（expense）

完成：

- expense 域基础 CRUD
- `amount_minor`
- `currency`
- `category`
- `spent_at`

完成标准：

- expense 基础接口可用
- capture / pending / expense 共用同一套底座
- 金额字段按约定正确落地
- 架构未发生破坏性调整

---

## Step 8：基础测试与收口

完成：

- 基础测试目录建立
- 至少覆盖核心服务启动
- 至少覆盖 1~3 个业务域的基础链路
- 文档与代码对齐检查

完成标准：

- 基础测试可运行
- 核心 API 链路可验证
- 配置文档与代码一致
- 无明显架构漂移

---

## 7. 第一阶段建议优先级

建议优先级如下：

### P0（必须完成）

- 文档规则基线
- API 工程骨架
- 公共层
- DB 基础设施
- capture 基础 CRUD

### P1（强烈建议完成）

- pending 基础 CRUD
- expense 基础 CRUD
- 基础测试
- 配置规范与示例文件同步

### P2（可以后置）

- knowledge 基础域实现
- health 基础域实现
- 更细的异常分类
- 更完整的测试覆盖

---

## 8. 第一阶段交付结果

Phase 1 结束时，至少应交付：

### 8.1 文档层

- 项目说明文档
- 架构规则文档
- 任务模板
- 数据模型草案
- env 规范
- 阶段路线图

### 8.2 工程层

- FastAPI 服务可启动
- SQLite 接入可运行
- 公共层已成型
- 总路由与系统级接口可用

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

---

## 9. 完成判定

若满足以下条件，则可视为 Phase 1 基础达成：

- API 项目结构清晰且稳定
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

- 还没把 CRUD 跑通就去做 Web
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

---

## 11. 当前阶段最重要的判断

TraceFold 在当前阶段最重要的，不是尽快做出很多功能页面，而是先确认：

- API 底座是否稳定
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

它是 Phase 1 的**执行顺序文档**，用于约束“先后顺序”和“阶段边界”，而不是用于堆产品想象。