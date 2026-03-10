# ENV CONVENTIONS

## 1. 目标

本文档定义 TraceFold 项目的环境变量（env）命名、归属、读取与变更规则。

Phase 1 的重点是先把 `apps/api` 的配置规范立住，避免后续由 Codex 实现代码时出现：

- 变量命名混乱
- 业务代码到处直接读取环境变量
- `.env.example`、文档、代码三者不一致
- 不同 app 后续接入时命名空间冲突

---

## 2. 当前适用范围

### 2.1 Phase 1 实际重点

Phase 1 只把以下范围作为强执行主线：

- `apps/api`

也就是说：

- 当前必须真正落地并严格执行的是 **API 的 env 规范**
- Web / Desktop / Bot 的命名空间可以预留，但不作为当前阶段的主要实现对象

### 2.2 后续预留范围

为后续扩展，本文档保留以下 app 的命名空间约定：

- `apps/web`
- `apps/desktop`
- `apps/bot`

但这些 app 在 Phase 1 中：

- 不要求完整实现
- 不要求配置项完全展开
- 不允许反向影响当前 API 规范设计

---

## 3. 核心原则

### 原则 1：环境变量是接口契约，不是随手新增的字符串

每个环境变量都必须有明确的：

- 名称
- owner app（归属应用）
- 是否必填
- 是否敏感
- 示例值
- 用途说明

不允许“先写进代码，之后再补文档”。

---

### 原则 2：变量必须带命名空间前缀

所有 app 自有变量都必须带 app 前缀，禁止使用模糊公共名。

当前已确定的命名空间规则：

- API 使用 `TRACEFOLD_API_`
- Web 预留使用 `TRACEFOLD_WEB_`
- Desktop 预留使用 `TRACEFOLD_DESKTOP_`
- Bot 预留使用 `TRACEFOLD_BOT_`

---

### 原则 3：配置统一入口读取

所有环境变量必须通过统一配置入口解析。

对于 API：

- 只允许 `apps/api/app/core/config.py` 读取环境变量
- 业务代码不得直接调用 `os.getenv()` / `os.environ`

---

### 原则 4：`.env.example` 不是规范真相源

每个 app 可以有自己的 `.env.example`，但规范真相源是：

1. 本文档 `docs/ENV_CONVENTIONS.md`
2. 对应 app 的统一配置入口（例如 API 的 `app/core/config.py`）

`.env.example` 只是示例文件，不是架构规则本身。

---

### 原则 5：敏感变量不得暴露到前端可见环境

任何密钥、数据库地址、私有 token、服务端专用配置，都不得进入前端可见环境。

即使未来实现 Web，也必须遵守：

- 前端只能读取明确允许公开的配置
- 服务端敏感配置只能由服务端读取

---

## 4. 命名规则

## 4.1 命名空间规则

### API

API 环境变量统一使用前缀：

- `TRACEFOLD_API_`

示例：

- `TRACEFOLD_API_APP_NAME`
- `TRACEFOLD_API_ENV`
- `TRACEFOLD_API_DEBUG`
- `TRACEFOLD_API_HOST`
- `TRACEFOLD_API_PORT`
- `TRACEFOLD_API_DB_URL`
- `TRACEFOLD_API_LOG_LEVEL`

### Web（预留）

Web 环境变量预留使用前缀：

- `TRACEFOLD_WEB_`

示例：

- `TRACEFOLD_WEB_ENV`
- `TRACEFOLD_WEB_PORT`
- `TRACEFOLD_WEB_API_BASE_URL`

### Desktop（预留）

Desktop 环境变量预留使用前缀：

- `TRACEFOLD_DESKTOP_`

示例：

- `TRACEFOLD_DESKTOP_ENV`
- `TRACEFOLD_DESKTOP_WEB_URL`
- `TRACEFOLD_DESKTOP_SINGLE_INSTANCE`

### Bot（预留）

Bot 环境变量预留使用前缀：

- `TRACEFOLD_BOT_`

示例：

- `TRACEFOLD_BOT_ENV`
- `TRACEFOLD_BOT_API_BASE_URL`
- `TRACEFOLD_BOT_TELEGRAM_TOKEN`

---

## 4.2 禁止使用的模糊变量名

以下变量名原则上禁止直接使用：

- `PORT`
- `HOST`
- `ENV`
- `DEBUG`
- `URL`
- `BASE_URL`
- `TOKEN`
- `KEY`
- `DATABASE_URL`

原因：

- 看不出属于哪个 app
- 看不出谁应该读取
- 多 app 共存时极易冲突
- Codex 很容易误判归属

必须改写成带命名空间和语义的名称，例如：

- `TRACEFOLD_API_PORT`
- `TRACEFOLD_API_DB_URL`
- `TRACEFOLD_WEB_API_BASE_URL`
- `TRACEFOLD_BOT_TELEGRAM_TOKEN`

---

## 4.3 命名风格

统一使用：

- 全大写
- 单词以下划线连接
- 名称尽量体现归属和用途

推荐：

- `TRACEFOLD_API_DB_URL`
- `TRACEFOLD_API_LOG_LEVEL`
- `TRACEFOLD_WEB_API_BASE_URL`

不推荐：

- `TRACEFOLD_API_DB`
- `TRACEFOLD_WEB_URL`
- `TRACEFOLD_BOT_TOKEN`

---

## 5. Owner 归属规则

每个环境变量必须有且只有一个 owner app。

owner 的含义是：

- 该变量主要由哪个 app 定义和维护
- 该变量应写入哪个 app 的 `.env.example`
- 该变量由哪个 app 的配置入口负责解析

例如：

- `TRACEFOLD_API_PORT` 的 owner 是 `api`
- `TRACEFOLD_WEB_API_BASE_URL` 的 owner 是 `web`
- `TRACEFOLD_BOT_TELEGRAM_TOKEN` 的 owner 是 `bot`

注意：

- 一个变量只能有一个 owner
- 变量可以被其他 app 间接依赖，但不能因此变成“共享无主变量”

---

## 6. API 读取规则（Phase 1 强制执行）

### 6.1 唯一读取入口

在 API 项目中，环境变量只允许在以下位置读取：

- `apps/api/app/core/config.py`

这是唯一合法入口。

---

### 6.2 禁止直接读取 env 的位置

禁止在以下位置直接读取环境变量：

- `apps/api/app/api/*.py`
- `apps/api/app/domains/**/*.py`
- `apps/api/app/db/*.py`
- `router.py`
- `service.py`
- `repository.py`
- `models.py`
- `schemas.py`

禁止形式包括但不限于：

- `os.getenv(...)`
- `os.environ[...]`
- `os.environ.get(...)`

---

### 6.3 正确使用方式

正确做法是：

1. 在 `app/core/config.py` 中统一解析 env
2. 构造统一配置对象，如 `settings`
3. 其他模块通过导入或依赖注入使用配置对象
4. 不在业务层重新解释 env 语义

---

## 7. 其他 app 的读取规则（预留）

虽然 Web / Desktop / Bot 尚未在 Phase 1 中完整落地，但规则提前统一：

- 每个 app 未来都必须有自己的统一配置入口
- 业务逻辑层不得散落式读取 env
- 不允许组件、页面、业务服务到处直接取环境变量

也就是说：

- 现在先把 API 规则定严
- 以后其他 app 按同样模式落地
- 不允许以后再回头打破 API 已确立的配置原则

---

## 8. 安全规则

## 8.1 可暴露给前端的变量

允许暴露给前端的变量必须满足：

- 不含密钥
- 不含数据库连接信息
- 不含私有 token
- 只用于公开运行时配置

示例：

- `TRACEFOLD_WEB_ENV`
- `TRACEFOLD_WEB_API_BASE_URL`

---

## 8.2 不可暴露给前端的变量

以下类型不得进入前端可见环境：

- 数据库连接串
- 私有第三方服务凭证
- Telegram Token
- OpenAI API Key
- 内部管理密钥
- 任何仅服务端需要的开关或连接信息

示例：

- `TRACEFOLD_API_DB_URL`
- `TRACEFOLD_BOT_TELEGRAM_TOKEN`
- `OPENAI_API_KEY`

---

## 9. 文件规则

项目采用“分 app 维护 `.env.example`，统一文档约束”的方式。

建议文件位置：

- `apps/api/.env.example`
- `apps/web/.env.example`
- `apps/desktop/.env.example`
- `apps/bot/.env.example`

但在 Phase 1 中，真正需要先落地的是：

- `apps/api/.env.example`

---

### 规则 1

每个 app 只维护自己的 `.env.example`。

---

### 规则 2

`.env.example` 中只放该 app 实际需要的变量。

不允许：

- 把未来可能用到但当前没实现的变量提前塞满
- 把别的 app 的变量写进当前 app 的 `.env.example`

---

### 规则 3

如变量变更，必须同步更新：

- 对应 app 的 `.env.example`
- 本文档 `docs/ENV_CONVENTIONS.md`
- 对应 app 的配置入口代码

对 API 来说就是：

- `apps/api/.env.example`
- `docs/ENV_CONVENTIONS.md`
- `apps/api/app/core/config.py`

三者必须同步。

---

## 10. 变量分类规则

为了避免 env 文件越来越乱，变量应尽量归入以下四类之一。

### 10.1 运行基础类

用于定义运行环境和基础行为。

示例：

- `TRACEFOLD_API_APP_NAME`
- `TRACEFOLD_API_ENV`
- `TRACEFOLD_API_DEBUG`
- `TRACEFOLD_API_HOST`
- `TRACEFOLD_API_PORT`
- `TRACEFOLD_API_LOG_LEVEL`

### 10.2 地址连接类

用于定义服务访问地址或数据库连接。

示例：

- `TRACEFOLD_API_DB_URL`
- `TRACEFOLD_WEB_API_BASE_URL`
- `TRACEFOLD_DESKTOP_WEB_URL`
- `TRACEFOLD_BOT_API_BASE_URL`

### 10.3 外部集成类

用于定义第三方系统接入配置。

示例：

- `TRACEFOLD_BOT_TELEGRAM_TOKEN`
- `OPENAI_API_KEY`

### 10.4 功能开关类

用于定义可开关的特性。

示例：

- `TRACEFOLD_API_DEBUG`
- `TRACEFOLD_API_ENABLE_DOCS`
- `TRACEFOLD_DESKTOP_SINGLE_INSTANCE`

如果某个变量无法明确归类，说明命名或设计可能有问题，应先整理再加入。

---

## 11. Phase 1 API 推荐变量清单

Phase 1 当前推荐只先确定 API 的最小必要变量。

| 变量名 | Owner | 必填 | 敏感 | 示例值 | 用途 |
|---|---|---:|---:|---|---|
| `TRACEFOLD_API_APP_NAME` | api | 是 | 否 | `TraceFold API` | API 应用名称 |
| `TRACEFOLD_API_ENV` | api | 是 | 否 | `development` | API 运行环境 |
| `TRACEFOLD_API_DEBUG` | api | 是 | 否 | `true` | 是否启用调试模式 |
| `TRACEFOLD_API_HOST` | api | 是 | 否 | `127.0.0.1` | API 监听地址 |
| `TRACEFOLD_API_PORT` | api | 是 | 否 | `8000` | API 监听端口 |
| `TRACEFOLD_API_DB_URL` | api | 是 | 是 | `sqlite:///../../data/tracefold.db` | API 使用的数据库连接 |
| `TRACEFOLD_API_LOG_LEVEL` | api | 是 | 否 | `INFO` | API 日志级别 |
| `TRACEFOLD_API_ENABLE_DOCS` | api | 否 | 否 | `true` | 是否启用 OpenAPI 文档 |

说明：

- `TRACEFOLD_API_ENABLE_DOCS` 可选，但很实用，适合开发期保留
- 如你想进一步收紧，也可以 Phase 1 先不加它

---

## 12. API `.env.example` 参考内容

以下内容可作为 `apps/api/.env.example` 初版：

```env
TRACEFOLD_API_APP_NAME=TraceFold API
TRACEFOLD_API_ENV=development
TRACEFOLD_API_DEBUG=true
TRACEFOLD_API_HOST=127.0.0.1
TRACEFOLD_API_PORT=8000
TRACEFOLD_API_DB_URL=sqlite:///../../data/tracefold.db
TRACEFOLD_API_LOG_LEVEL=INFO
TRACEFOLD_API_ENABLE_DOCS=true
```

---

## 13. 预留命名空间示例（非 Phase 1 必落地）

以下只是为了统一未来命名风格，不代表当前就要实现这些 app。

### 13.1 Web

```env
TRACEFOLD_WEB_ENV=development
TRACEFOLD_WEB_PORT=5173
TRACEFOLD_WEB_API_BASE_URL=http://127.0.0.1:8000
```

### 13.2 Desktop

```env
TRACEFOLD_DESKTOP_ENV=development
TRACEFOLD_DESKTOP_WEB_URL=http://127.0.0.1:5173
TRACEFOLD_DESKTOP_SINGLE_INSTANCE=true
```

### 13.3 Bot

```env
TRACEFOLD_BOT_ENV=development
TRACEFOLD_BOT_API_BASE_URL=http://127.0.0.1:8000
TRACEFOLD_BOT_TELEGRAM_TOKEN=
```

这些内容当前只用于预留命名空间，不作为 Phase 1 的主实现要求。

---

## 14. 配置变更流程

新增、删除或修改环境变量时，必须同时完成以下步骤。

### 14.1 新增变量

必须同步修改：

1. 对应 app 的 `.env.example`
2. 本文档 `docs/ENV_CONVENTIONS.md`
3. 对应 app 的配置入口代码

---

### 14.2 删除变量

必须同步修改：

1. 删除 `.env.example` 中对应项
2. 删除配置入口中的对应字段
3. 更新本文档说明

---

### 14.3 重命名变量

禁止只改其中一处。

必须同时修改：

- 配置入口
- `.env.example`
- 本文档
- 所有引用代码

---

## 15. Codex 实现规则

Codex 后续在本仓库中工作时，必须遵守以下 env 规范：

1. 不得在 `app/core/config.py` 之外直接读取 API 环境变量
2. 不得引入模糊变量名
3. 新增变量必须带命名空间前缀
4. 新增变量必须声明 owner app
5. 涉及配置变更时，必须同步更新：
   - `.env.example`
   - 本文档
   - 配置入口代码
6. 不得把服务端敏感变量暴露给前端 app
7. 不得因为未来 Web / Desktop / Bot 需求，破坏当前 API 配置规则

---

## 16. Phase 1 铁律

第一阶段最重要的 env 约束只有这几条：

- API 变量统一使用 `TRACEFOLD_API_` 前缀
- 每个变量必须有明确 owner
- API 只允许 `app/core/config.py` 读取 env
- `.env.example` 只是示例，不是真相源
- 任何配置变更必须同步更新文档和配置入口
- Phase 1 先以 API 配置规则为主，不让其他 app 的预留设计反向干扰当前实现

---

## 17. 本文档的定位

本文档不是为了罗列很多变量，而是为了定义：

- 变量应该怎么命名
- 变量归谁负责
- 变量在哪里读取
- 变量变更时必须同步哪些地方
- 为什么 Phase 1 要先把 API 的配置规则立住

它是 TraceFold Phase 1 的配置规范文档，也是后续约束 Codex 实现 `config.py` 和 `.env.example` 的直接依据。