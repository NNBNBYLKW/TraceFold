# TraceFold Telegram Bot 设置教程

## 1. 这份教程适用于什么

这份教程适用于当前 TraceFold 仓库里的 Telegram adapter。

它的定位是轻入口，不是管理端。当前 Telegram 这条线只承担：

- 私聊纯文本 quick capture 输入
- 最小 `/start`
- 最小 `/help`

它不是：

- Workbench 替代界面
- 模板系统入口
- `force_insert` 入口
- 长 AI summary 界面
- 第二业务中心

## 2. Telegram 官方通用准备

下面这些是 Telegram 官方通用基础，和任何 Bot 都有关。

### 2.1 用 @BotFather 创建 Bot

Telegram 官方的 Bot 创建入口是 `@BotFather`。你需要先在 Telegram 里找到它，然后按提示创建一个新的 bot。创建完成后，你会拿到一个 bot token。

官方参考：

- Telegram Introduction to Bots: <https://core.telegram.org/bots>

### 2.2 Bot token 是敏感凭证

官方文档明确说明：bot token 是 bot 的唯一认证凭证，拿到它的人就等于能完全控制这个 bot。

所以你应该：

- 只把 token 放进本地 `.env`
- 不要提交到仓库
- 不要发到聊天群
- 一旦怀疑泄露，就回 BotFather 重新生成

官方参考：

- Telegram Introduction to Bots: <https://core.telegram.org/bots>

### 2.3 Telegram Bot API 是基于 HTTP 的接口

Telegram 官方把 Bot API 定义为一个 HTTP-based interface。你对 Telegram bot 的大部分调用，本质上都是发 HTTP 请求到 Telegram 的 Bot API。

官方参考：

- Telegram Bot API: <https://core.telegram.org/bots/api>

### 2.4 用户必须先和 bot 建立对话

Telegram 官方明确说明：bot 不能主动先和用户发起私聊。用户必须先：

- 主动给 bot 发消息
- 或先把 bot 加入群

否则 bot 不能先给你发消息。

官方参考：

- Telegram Introduction to Bots: <https://core.telegram.org/bots>

### 2.5 getUpdates 和 webhook 是两种互斥的收更新方式

Telegram 官方说明，接收更新有两条路：

- `getUpdates`
- `setWebhook`

这两种方式是互斥的。设置了 outgoing webhook 之后，就不能再同时用 long polling 的 `getUpdates`。

官方参考：

- Telegram Bot API: <https://core.telegram.org/bots/api>
- Bots FAQ: <https://core.telegram.org/bots/faq>

### 2.6 更新在服务器上最多保留 24 小时

官方文档明确写了：incoming updates 会暂存在 Telegram 服务器上，但不会保留超过 24 小时。

这意味着：

- 你的 bot 如果长期不取更新，旧更新会过期
- polling / webhook 不是“以后再取也没事”的无限队列

官方参考：

- Telegram Bot API: <https://core.telegram.org/bots/api>

### 2.7 webhook 需要 Telegram 能访问你的 URL

如果你选 webhook，前提是 Telegram 能访问你提供的 HTTPS URL。官方文档也列出了 webhook 的端口和证书要求。

这对本地开发者意味着：一个只在你本机可见、Telegram 访问不到的 URL，不能直接拿来做正式 webhook 接收入口。

官方参考：

- Telegram Bot API: <https://core.telegram.org/bots/api>
- Telegram webhook guide: <https://core.telegram.org/bots/webhooks>

### 2.8 群聊默认还有隐私模式

Telegram 官方说明：bot 加入群之后，默认并不会自动看到所有消息。默认群场景下它只会看到和它相关的消息，这和 privacy mode 有关。

所以如果你把 bot 拉进群里，但发现它“看不到大家随便说的话”，这通常不是 TraceFold 的问题，而是 Telegram bot 默认行为。

官方参考：

- Telegram Introduction to Bots: <https://core.telegram.org/bots>
- Bots FAQ: <https://core.telegram.org/bots/faq>

### 2.9 为什么当前 TraceFold 教程先按 polling / getUpdates 口径理解

当前 TraceFold 仓库里的 Telegram client 已经实现了 `getUpdates()` 调用，没有 webhook 实现面，也没有 `setWebhook` 配置面。

更重要的是，TraceFold 当前阶段仍是 local-first、轻入口、最小 adapter 路线。对本地自用和当前仓库现实来说，先按 polling / getUpdates 口径理解，是更自然也更贴近当前实现的。

但请注意：当前仓库的**正式启动入口**现在已经可以进入最小 polling 循环，但它仍然不是复杂 bot runtime 或管理端，这一点会在下面的 TraceFold 专用部分明确说明。

## 3. TraceFold 当前 Telegram adapter 的职责边界

按当前仓库现实，Telegram adapter 当前支持：

- 私聊纯文本 capture
- `/start`
- `/help`

按当前仓库现实，Telegram adapter 当前不支持：

- 模板 CRUD
- workbench 模式系统
- `force_insert`
- 多轮修正状态机
- Knowledge / Health 详情页式阅读
- 复杂通知平台
- 独立业务逻辑

相关仓库依据：

- `apps/telegram/app/bot/handlers.py`
- `apps/telegram/app/bot/dispatch.py`
- `apps/telegram/app/formatting.py`
- `apps/telegram/tests/test_telegram_final_consistency.py`
- `docs/PHASE3_WAVE2_ADAPTER_CONTRACT_BASELINE.md`

## 4. 启动前准备

### 4.1 先准备哪些服务

对当前 Telegram adapter 来说，最关键的前置依赖是 TraceFold API。

最小前置顺序建议：

1. 先启动 TraceFold API
2. 再配置 Telegram adapter
3. 再运行 Telegram adapter 的正式入口

Web Workbench 不是 Telegram adapter 启动的强制依赖，但如果你想在 Web 里查看 pending / formal records / dashboard，对应的 Web 服务也应该启动。

### 4.2 `.env.example` 在哪里

当前示例文件在：

- `apps/telegram/.env.example`

### 4.3 当前仓库实际环境变量

当前仓库实际使用这些环境变量：

- `TRACEFOLD_TELEGRAM_BOT_TOKEN`
  - 你的 Telegram bot token
- `TRACEFOLD_TELEGRAM_API_BASE_URL`
  - TraceFold API 根地址，当前示例是 `http://127.0.0.1:8000/api`
- `TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS`
  - Telegram adapter 对外部 HTTP 调用的 timeout
- `TRACEFOLD_TELEGRAM_DEBUG`
  - 调试开关
- `TRACEFOLD_TELEGRAM_LOG_ENABLED`
  - 日志开关

### 4.4 当前 `.env.example` 怎么填

最小示例可以按这个思路：

```env
TRACEFOLD_TELEGRAM_BOT_TOKEN=你的_BotFather_token
TRACEFOLD_TELEGRAM_API_BASE_URL=http://127.0.0.1:8000/api
TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS=5
TRACEFOLD_TELEGRAM_DEBUG=false
TRACEFOLD_TELEGRAM_LOG_ENABLED=true
```

注意：

- `TRACEFOLD_TELEGRAM_BOT_TOKEN` 不能为空
- `TRACEFOLD_TELEGRAM_API_BASE_URL` 必须是 `http://` 或 `https://` 开头
- timeout 必须大于 0

这些约束在 `apps/telegram/app/core/config.py` 里有实际校验。

## 5. TraceFold Telegram adapter 实际启动步骤

这一节必须按**仓库现实**来理解，而不是按“理想中的完整 bot runtime”来写。

### 5.1 先启动 TraceFold API

当前仓库已有明确 API 启动基线。最常见本地启动方式是：

```powershell
cd apps/api
uvicorn app.main:app --host 127.0.0.1 --port 8000
```

然后确认：

- `http://127.0.0.1:8000/api/healthz` 可达

### 5.2 准备 Telegram adapter 的 `.env`

把 `apps/telegram/.env.example` 复制成 `apps/telegram/.env`，并填好 token 和 API base URL。

### 5.3 当前仓库的正式启动入口

当前仓库最贴近现实的 Telegram adapter 启动方式是：

```powershell
cd apps/telegram
python -m app.main
```

### 5.4 必须如实说明的仓库现实

当前 `apps/telegram/app/main.py` 的正式入口会：

- 创建 Telegram adapter app
- 探测 TraceFold API 是否可达
- 探测 Telegram Bot API `getMe` 是否可达
- 然后进入当前实现层的最小 polling 循环

也就是说，当前正式入口已经不是“只做 startup probe 就退出”的状态了。

仓库里确实已经有：

- `TelegramApiClient.get_updates()`
- `TelegramAdapterApp.process_updates_once()`
- `TelegramAdapterApp.run_polling()`

当前正式入口现在已经把这些能力收成了一个**最小可运行的 polling 入口**，可以持续接收 Telegram 文本消息，直到你手动停止进程。

所以这份教程会分清两件事：

1. 当前仓库已经支持并测试的 Telegram 轻量命令面和 text-to-capture 入口
2. 当前正式入口已经足够承担最小 lightweight adapter 运行，但它仍然不是复杂 bot runtime、管理端或工作流系统

## 6. 第一次验证怎么做

### 6.1 官方前置验证

先在 Telegram 客户端里：

1. 找到你刚创建的 bot
2. 先给它发一条消息，例如 `/start`

这是 Telegram 官方前提。用户不先和 bot 建立对话，bot 不能主动给你发消息。

### 6.2 TraceFold 侧最小接通验证

按当前仓库现实，第一轮最小验证建议分三步。

第一步：启动最小运行入口

```powershell
cd apps/telegram
python -m app.main
```

这一步如果没有在启动阶段报错，并且进程继续保持运行，说明至少这些东西是通的：

- `TRACEFOLD_TELEGRAM_BOT_TOKEN` 可用
- Telegram Bot API `getMe` 可用
- `TRACEFOLD_TELEGRAM_API_BASE_URL` 指向的 TraceFold API 可用

第二步：在 Telegram 客户端里做最小人工验证

- 发 `/start`
- 发 `/help`
- 再发一条普通纯文本消息

当前仓库现实里，这三种输入分别对应：

- 最小启动说明
- 最小帮助说明
- one message -> one capture 的轻量 capture-first 提交

第三步：确认当前命令面和 handler 语义是已实现并受 tests 保护的

当前仓库通过 tests 已经验证了这些命令会走统一 API 语义：

- plain text private capture
- `/start`
- `/help`

如果你想确认当前仓库实现面，最直接的方式是跑 Telegram tests：

```powershell
python -m pytest -q apps\telegram\tests
```

### 6.3 这里要特别诚实的一点

如果你期待的是：

- 启动一个常驻 Telegram bot 进程
- 然后直接在手机里发命令，bot 立刻持续回你

那么按**当前仓库现实**，现在已经有一个最小可用的日常运行入口了：

```powershell
cd apps/telegram
python -m app.main
```

但它仍然只是 lightweight adapter 级别的最小 polling runtime，不是复杂 bot runtime 产品。

当前已经实现的是：

- adapter 轻量命令语义
- API client
- 最小 polling runtime
- 测试覆盖

当前还没有追求的是：

- 复杂 bot runtime 管理能力
- richer command surface
- 管理端式交互

这不是 Telegram 职责边界的问题，而是当前仓库对 lightweight adapter 的刻意克制。

## 7. 当前推荐测试命令

按当前仓库现实，当前真正支持的命令是：

- `/start`
  - 启动欢迎信息
- `/help`
  - 说明轻量使用方式

另外，私聊里直接发纯文本，也会走 capture 路径。

按当前 tests 和 handlers 的现实，Telegram 当前只承担轻量 text-to-capture 适配器职责，不代表 Telegram 已经成为完整运行工作台。

## 8. 常见问题排查

### 8.1 token 配错

现象：

- `python -m app.main` 启动时在 Telegram 探测阶段失败

先查：

- `TRACEFOLD_TELEGRAM_BOT_TOKEN` 是否填对
- token 是否有多余空格
- token 是否已经在 BotFather 被重置

### 8.2 API base URL 配错

现象：

- Telegram adapter 能启动到一半，但 TraceFold API 探测失败
- 命令层 tests 或运行时表现为 `TraceFold API is unavailable`

先查：

- `TRACEFOLD_TELEGRAM_API_BASE_URL` 是否是 `http://127.0.0.1:8000/api` 这种完整 API 根地址
- `/api/healthz` 是否可达

### 8.3 bot 没收到消息

先查：

- 你是否已经先给 bot 发过消息
- 如果在群里，bot 是否只是因为 Telegram 默认 privacy mode 看不到不相关消息
- 当前仓库是否真的有一个长期运行的 polling 进程在取 updates

### 8.4 polling 没有真正跑起来

这是当前仓库最容易让人误会的一点。

当前正式入口：

```powershell
cd apps/telegram
python -m app.main
```

会先做依赖探测，然后进入当前实现层的最小 polling 循环。

它仍然不是复杂 bot runtime 或管理端，只是当前仓库里最小可用的 text-to-capture 适配器入口。

### 8.5 Telegram 命令能发出，但 TraceFold 后端不可达

当前 handler/formatter 的短错误反馈大致会是：

- `Not recorded. Service unavailable. Try again later.`
- `Not recorded. Input is invalid.`
- `Not recorded. Try again.`

这类报错通常说明：

- Telegram adapter 这一层还在
- 但共享 TraceFold API 不可达，或者返回失败

它不等于“正式事实层已经坏了”。

### 8.6 adapter 有反馈，但不是用户以为的业务层失败

例如：

- `Text is required.`
- `Not recorded. Input is invalid.`
- `Only /start and /help are available. Send plain text to record quickly.`

这些通常是：

- adapter 的最小输入校验反馈
- 或共享 API 的业务错误被短文本映射之后的结果

它不代表 Telegram 自己拥有独立业务逻辑。

## 9. 当前限制与边界提醒

请把当前 Telegram adapter 理解成：

- lightweight adapter
- quick capture surface
- 统一 HTTP API 的轻入口

不要把它理解成：

- 管理端
- Web workbench 替代界面
- pending 审核入口
- 模板系统入口
- AI 长摘要界面
- 第二业务中心

还要再提醒一次当前仓库现实限制：

- 当前正式入口是最小 polling 入口
- 它仍然不是复杂 bot runtime 或管理端

所以这份教程是一个**与当前仓库真实实现对齐**的设置教程，不是一个“理想状态 Telegram 产品手册”。

## 10. 最短成功路径总结

如果你是新手，照着下面这份最短 checklist 做：

1. 在 Telegram 里找到 `@BotFather`
2. 创建 bot，拿到 token
3. 把 token 填进 `apps/telegram/.env`
4. 先启动 TraceFold API
5. 再执行：

```powershell
cd apps/telegram
python -m app.main
```

6. 在 Telegram 客户端里先给 bot 发一条消息，例如：

```text
/start
```

7. 如果你要确认当前仓库命令面已经实现，运行：

```powershell
python -m pytest -q apps\telegram\tests
```

8. 记住当前现实边界：
   - 命令面已经实现并测试
   - 正式入口当前会先做 probe，再进入最小 polling 循环
   - 它已经能承担轻量 text-to-capture adapter，但仍然不是复杂 bot runtime 或管理端
