# ARCHITECTURE RULES

## 1. 目标

本文档定义 TraceFold API 的强制架构规则。

所有新增代码、重构、扩展都必须遵守本文档。
如某个实现任务与本文档冲突，默认以本文档为准；除非该任务的目标本身就是修改架构规则，并且先同步更新本文档。

---

## 2. 当前架构立场（Phase 1）

TraceFold API 在第一阶段采用以下固定立场：

1. **业务域 + 公共层并存**
   - 业务能力按域组织
   - 基础设施按公共层统一收口

2. **本地优先（local-first）**
   - 当前优先服务单用户、本地运行场景
   - 暂不为多租户、分布式、复杂权限体系增加结构复杂度

3. **SQLite 是唯一真相源**
   - 第一阶段所有结构化数据统一进入 SQLite
   - 不允许业务域各自引入新的数据库或私有真相源

4. **先保证可维护，再追求功能速度**
   - 优先保证目录清晰、职责明确、依赖方向稳定
   - 不为了短期开发速度破坏边界

---

## 3. 标准目录结构

TraceFold API 目录结构必须保持如下主形态：

```text
apps/api/
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
│     │  ├─ router.py
│     │  ├─ service.py
│     │  ├─ repository.py
│     │  ├─ models.py
│     │  └─ schemas.py
│     ├─ pending/
│     │  ├─ router.py
│     │  ├─ service.py
│     │  ├─ repository.py
│     │  ├─ models.py
│     │  └─ schemas.py
│     ├─ expense/
│     │  ├─ router.py
│     │  ├─ service.py
│     │  ├─ repository.py
│     │  ├─ models.py
│     │  └─ schemas.py
│     ├─ knowledge/
│     │  ├─ router.py
│     │  ├─ service.py
│     │  ├─ repository.py
│     │  ├─ models.py
│     │  └─ schemas.py
│     └─ health/
│        ├─ router.py
│        ├─ service.py
│        ├─ repository.py
│        ├─ models.py
│        └─ schemas.py
├─ tests/
├─ .env.example
└─ README.md