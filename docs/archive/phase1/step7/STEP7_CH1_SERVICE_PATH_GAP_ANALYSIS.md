# STEP 7 CHAPTER 1 SERVICE PATH GAP ANALYSIS

## Purpose

本文档用于整理 Step 7 / Chapter 1 在统一服务通路上的代码现状与最小补口方向。

分析范围仅包括：

- Capture submit HTTP 闭环
- Pending minimal review action HTTP 闭环
- Telegram / Desktop Step 7 会依赖的最小摘要与状态读取接口
- 统一错误返回语义
- 对应测试覆盖缺口

本文档不提出大规模重构，不引入第二套入口逻辑，也不把 Telegram / Desktop 的交互需求直接塞进 service 语义。

---

## Existing Reusable Service Capabilities

### 1. Capture / intake 主链服务已存在

当前已有可复用能力：

- `app/services/intake/service.py`
  - `submit_capture(...)`
  - `parse_capture(...)`
  - `process_capture(...)`
- `app/domains/capture/service.py`
  - `submit_capture(...)`
  - `save_parse_result(...)`

现状判断：

- `app/services/intake/service.py` 已经覆盖“提交 capture -> 解析 -> 路由到 pending 或正式记录”的主链语义。
- 这条链路已经能决定：
  - 进入 `pending`
  - 直接写入 `expense / knowledge / health`
- 这里是 Chapter 1 最值得直接复用的现有 service 能力。

当前限制：

- `process_capture(...)` 返回原始 `dict`，没有对应 HTTP response schema。
- `submit_capture(...)` / `parse_capture(...)` / `process_capture(...)` 主要是 `flush` 风格，没有形成统一的 HTTP 事务边界。
- `app/domains/capture/service.py` 与 `app/services/intake/service.py` 存在名称重叠，router 直接接线时容易出现职责选择不清。

结论：

- Chapter 1 不需要重写 capture 主链逻辑。
- 更合适的做法是增加一个薄的 HTTP facade service，统一调用现有 intake service，并负责 HTTP 层需要的返回形态与事务收口。

### 2. Pending review service 闭环已存在

当前已有可复用能力：

- `app/domains/pending/service.py`
  - `list_pending_reads(...)`
  - `get_pending_read(...)`
  - `fix_pending_item(...)`
  - `confirm_pending_item(...)`
  - `discard_pending_item(...)`
  - `force_insert_pending_item(...)`

现状判断：

- Pending review 的核心业务逻辑已经在 service 层闭环。
- 状态校验、payload 校验、正式写入、review action 记录、capture 状态更新都已收口在 service 层。
- 这些能力适合作为 Chapter 1 的直接复用基础。

当前限制：

- 写操作 service 返回的是 `PendingItem` ORM 对象，不是现成的 HTTP 写操作 response model。
- `force_insert_pending_item(...)` 虽然已存在，但根据 Step 7 冻结结论，不能作为 Telegram 依赖路径。

结论：

- Chapter 1 应直接复用 `fix / confirm / discard` 三个 service。
- `force_insert_pending_item(...)` 保持 service 能力存在，但不应纳入 Step 7 对外最小 HTTP 写接口。

### 3. Dashboard / alerts / pending read 能力已存在

当前已有可复用能力：

- `app/domains/dashboard/service.py`
  - `get_dashboard_read(...)`
- `app/domains/alerts/service.py`
  - `list_alert_reads(...)`
  - `mark_alert_as_viewed(...)`
  - `dismiss_alert(...)`
- `app/domains/pending/service.py`
  - `list_pending_reads(...)`
  - `get_pending_read(...)`

现状判断：

- `dashboard` 已经提供 pending summary、alert summary、recent activity。
- `alerts` 已经提供 health alert 列表和状态消费接口。
- `pending` 已经提供最小列表与详情读取。

结论：

- Step 7 Telegram / Desktop 所需的“轻量查看”主干已基本存在。
- Chapter 1 的主要缺口不在读取主干，而在写入通路和返回一致性。

---

## Existing HTTP Capabilities

当前已存在的 HTTP 接口能力：

- `GET /api/dashboard`
- `GET /api/pending`
- `GET /api/pending/{id}`
- `GET /api/alerts`
- `POST /api/alerts/{id}/viewed`
- `POST /api/alerts/{id}/dismissed`
- `GET /api/expense`
- `GET /api/expense/{id}`
- `GET /api/knowledge`
- `GET /api/knowledge/{id}`
- `GET /api/health`
- `GET /api/health/{id}`
- `GET /api/ping`
- `GET /api/healthz`

现状判断：

- `dashboard`、`pending read`、`alerts`、`formal read` 已有 HTTP 读取面。
- `capture` router 已挂载，但当前为空。
- `pending` router 当前只有读取接口，没有 review action 写接口。

对 Step 7 / Chapter 1 的含义：

- Telegram 的轻量查看基础主要可复用 `dashboard`、`pending read`、`alerts`。
- Desktop 的服务状态可见基础可复用 `/api/ping`、`/api/healthz`。
- 当前真正缺的是“统一写入 HTTP 通路”。

---

## Missing HTTP Write Paths

### 1. Capture submit HTTP 闭环缺失

现状：

- `capture/router.py` 为空。
- 虽然已有 intake service，但没有 HTTP 层可调用的统一入口。

缺口：

- 缺 `POST /api/capture` 之类的最小提交入口。
- 缺能够表达处理结果的 response schema。
- 缺 HTTP 层对 capture 主链事务边界的明确收口。

最小补口建议：

- 新增一个薄 router：
  - 接收 `CaptureSubmitRequest`
  - 调用薄 facade service
  - 返回统一 `ApiResponse`
- 新增一个薄 facade service：
  - 复用 `app/services/intake/service.py`
  - 负责一次性执行 submit + process
  - 负责 commit / rollback
  - 负责把原始 `dict` 结果适配成清晰的 HTTP response model

不建议：

- 在 router 中自己串写 submit / parse / pending / formal-write 逻辑
- 为 Telegram 单独发明 capture 语义

### 2. Pending minimal review action HTTP 闭环缺失

现状：

- `pending/service.py` 已有 `fix / confirm / discard`
- `pending/schemas.py` 已有：
  - `PendingFixRequest`
  - `PendingConfirmRequest`
  - `PendingDiscardRequest`
  - `PendingForceInsertRequest`
- `pending/router.py` 当前只暴露读取接口

缺口：

- 缺 `POST /api/pending/{id}/fix`
- 缺 `POST /api/pending/{id}/confirm`
- 缺 `POST /api/pending/{id}/discard`

最小补口建议：

- 直接复用 `pending/service.py` 的现有写操作 service
- router 保持薄，只做：
  - 参数接收
  - 调用 service
  - 返回统一 response model

额外约束：

- `force_insert` 不应进入 Step 7 对外最小接口集合
- 即使未来需要保留 HTTP 入口，也不应作为 Telegram 依赖路径

### 3. Pending 写操作返回模型尚未收口

现状：

- 写操作 service 返回 `PendingItem`
- 读取接口主要返回：
  - `PendingListRead`
  - `PendingDetailRead`
  - `PendingItemRead`

缺口：

- 当前没有明确约定 review action 写接口应返回哪一种统一读模型

最小补口建议：

- 优先复用现有 `PendingDetailRead` 或 `PendingItemRead`
- 如两者都不够贴合，再增加一个很薄的写操作响应 read model

不建议：

- 让 router 直接返回 ORM
- 为 Telegram 单独定义专用写入返回语义

---

## Missing Read/Summary Paths For Step 7 Entries

### 1. Pending minimal visibility 基本已有

现状：

- `GET /api/pending` 已提供列表、分页、过滤、`next_pending_item_id`
- `GET /api/pending/{id}` 已提供单条详情

判断：

- 对 Telegram 的最小 pending 查看来说，这两条读取路径已基本够用。
- 对 Desktop Shell 来说，这两条路径也不需要额外变形。

### 2. Dashboard / alerts lightweight visibility 基本已有

现状：

- `GET /api/dashboard` 已提供：
  - pending summary
  - alert summary
  - recent activity
- `GET /api/alerts?source_domain=health` 已提供 alert 列表读取

判断：

- 对 Telegram 的轻量 dashboard / alerts 查看来说，当前主干已存在。
- 当前没有明显证据表明还需要专门为 Telegram / Desktop 再开一套摘要接口。

### 3. Service visibility 路径存在，但风格未统一

现状：

- `GET /api/ping`
- `GET /api/healthz`

判断：

- Desktop Shell 可复用这两个路径做服务状态可见。
- 但这两个接口当前返回 plain dict，而非标准 `ApiResponse` 包装。

结论：

- 当前更像“风格一致性缺口”，而不是“能力不存在”。
- Chapter 1 可以选择：
  - 保持现状，先复用
  - 或做一个非常小的统一包装修补

### 4. 当前不建议新增的读取路径

当前不建议在 Chapter 1 新增：

- Telegram 专用 pending 摘要接口
- Desktop 专用 dashboard 接口
- Telegram 专用 alert 接口

原因：

- 现有读取能力已经能覆盖 Step 7 的轻量查看范围
- 新增专用读取接口容易把入口差异写成语义差异

### 5. Chapter 1 读取面复查结论

完成复查后，当前结论如下：

- `pending summary` 可直接复用 `GET /api/dashboard` 中的 `pending_summary`
- `minimal dashboard summary` 可直接复用 `GET /api/dashboard`
- `minimal alerts visibility` 可直接复用：
  - `GET /api/dashboard` 中的 `alert_summary`
  - `GET /api/alerts?source_domain=health`
- `single pending detail for action context` 可直接复用 `GET /api/pending/{id}`
- `service health/status visibility` 可直接复用：
  - `GET /api/ping`
  - `GET /api/healthz`

因此，Chapter 1 当前不新增新的 summary / status 读取接口。

本次复查的判断依据是：

- 现有 `dashboard` 已提供 pending 与 alert 的轻量摘要
- 现有 `alerts` 已提供 health alert 最小可见性
- 现有 `pending` detail 已足够支撑单条 action context
- 现有 `ping / healthz` 已足够支撑桌面壳的最小状态可见

当前保留的缺口只有风格一致性层面：

- `/api/ping` 与 `/api/healthz` 尚未使用统一 `ApiResponse` envelope

该缺口本轮不处理，原因是：

- 它不阻塞 Step 7 入口获得最小状态可见性
- 当前任务目标是避免重复造轮子，而不是扩张系统状态接口层

---

## Error/Response Consistency Gaps

### 1. 主体错误封装已存在

当前已有统一基础：

- `app/core/exceptions.py`
- `app/core/responses.py`

现状判断：

- 大多数正式 domain HTTP 接口已经使用 `ApiResponse[...]`
- service 层已使用 `BadRequestError / NotFoundError / ConflictError`
- 这为 Chapter 1 提供了现成错误语义基础

### 2. capture 写通路缺少统一 response model

现状：

- `CaptureSubmitRequest` 已存在
- `CaptureRecordRead`、`ParseResultRead` 已存在
- 但没有“capture 提交并处理后的统一结果模型”

影响：

- HTTP 层无法清晰返回：
  - capture 是否已进入 pending
  - 是否已直接 committed
  - 关联的 `pending_item_id` / `record_id`

建议：

- 新增一个最小 capture process response schema
- 不直接暴露 intake service 的原始 `dict`

### 3. system 状态接口没有走统一 envelope

现状：

- `/api/ping` 和 `/api/healthz` 返回 plain dict

影响：

- Desktop Shell 如直接消费这些路径，需要单独处理不同的响应结构
- Step 7 的服务状态读取语义不够统一

建议：

- 在 Chapter 1 文档中标记为一致性缺口
- 是否修补可作为最小附带改动决定，不需要大改

### 4. capture 与 pending 的事务风格不一致

现状：

- `pending/service.py` 写操作自带 `commit / rollback`
- `app/services/intake/service.py` 主要是 `flush`

影响：

- 如果直接从 router 调 intake service，容易把事务决策散落到 HTTP 层

建议：

- Capture HTTP 写通路增加薄 facade service 统一事务收口
- 不建议把事务拼装写进 router

### 5. Step 7 需要显式避免的错误暴露

Chapter 1 在设计 HTTP 写通路时，应显式避免：

- 暴露 `force_insert` 给 Telegram 依赖面
- 在 HTTP 层制造 entry-specific formal-write semantics
- 在 router 中加入“失败后临时补救”的隐藏逻辑

---

## Testing Gaps

### 1. Capture HTTP 集成测试缺失

现状：

- `apps/api/tests/domains/capture/test_capture_smoke.py` 仍是 skip

缺口：

- 没有覆盖 `POST /api/capture`
- 没有覆盖 capture -> pending / committed 两条结果路径
- 没有覆盖统一错误返回结构

### 2. Pending 写操作 HTTP 测试缺失

现状：

- `apps/api/tests/domains/pending/test_pending_review_service.py` 已覆盖 service 层闭环
- `apps/api/tests/domains/pending/test_pending_read.py` 已覆盖读取接口

缺口：

- 没有覆盖 `fix / confirm / discard` 的 HTTP 写接口
- 没有覆盖写接口的错误 envelope
- 没有覆盖“已 resolved 再操作”的 HTTP 级冲突语义

### 3. 读取面测试总体可复用

现状：

- `dashboard` 读取测试已存在
- `alerts` 读取与状态消费测试已存在
- `formal_read` 测试已存在

判断：

- Chapter 1 不需要为这些已稳定读取能力重建大批新测试
- 只需在必要时补最小集成联动测试

### 4. 测试数据库隔离需要延续现有模式

现状：

- `pending_read`、`dashboard`、`formal_read` 等测试都使用 `tmp_path` SQLite
- 通过 `dependency_overrides[get_db]` 隔离测试数据库

结论：

- Chapter 1 新增 HTTP 集成测试应继续使用同样模式
- 不应依赖默认数据库路径

---

## Proposed Minimal Implementation Order

### 1. 先补 capture 的统一 HTTP facade

目标：

- 建立 `POST /api/capture` 最小闭环

实现方向：

- 复用 `app/services/intake/service.py`
- 增加薄 facade service 统一事务与返回模型
- router 保持薄

### 2. 再补 pending 的三个最小写接口

目标：

- 建立：
  - `POST /api/pending/{id}/fix`
  - `POST /api/pending/{id}/confirm`
  - `POST /api/pending/{id}/discard`

实现方向：

- 直接复用 `app/domains/pending/service.py`
- 不暴露 `force_insert` 到 Step 7 最小入口面

### 3. 收口写接口 response model

目标：

- 让 capture 写接口和 pending 写接口都有清晰统一的 response schema

实现方向：

- capture 新增最小 process response schema
- pending 优先复用现有 read model，不够再补薄模型

### 4. 只在必要时补最小读取一致性修补

目标：

- 判断 `/api/ping`、`/api/healthz` 是否需要统一 envelope

实现方向：

- 如 Desktop Shell 确实需要一致消费，再做最小标准化
- 如当前够用，可先记录为一致性缺口，不阻塞 Chapter 1 主线

### 5. 最后补测试

最小优先级如下：

1. capture HTTP integration tests
2. pending write HTTP integration tests
3. error envelope tests for invalid / not found / already resolved
4. 复用现有 dashboard / alerts / formal read 测试，不做无关扩张

---

## Current Recommendation

Step 7 / Chapter 1 当前最合理的最小补口方式是：

- 不重写现有主链 service
- 不新发明 Telegram / Desktop 专用 service 语义
- 用薄 router + 薄 facade service 把已有 capture / pending 主链能力接成统一 HTTP 写通路
- 保持 dashboard / pending read / alerts 作为现有轻量读取基础

这样可以在不大改项目结构的前提下，补齐 Telegram / Desktop Step 7 真正依赖的统一服务通路，同时继续把业务真逻辑锁在 service 层。

---

## Final Chapter 1 Conclusion

Chapter 1 完成后的最终结论如下：

- `capture` 已有统一 HTTP 写入口
- `pending` 已有统一 HTTP action 写入口
- Step 7 最小读取能力已主要复用：
  - `GET /api/pending`
  - `GET /api/pending/{id}`
  - `GET /api/dashboard`
  - `GET /api/alerts`
  - `GET /api/ping`
  - `GET /api/healthz`
- Chapter 1 未新增 Telegram / Desktop 专用摘要接口
- Chapter 1 的核心收口点是：
  - 统一写通路
  - 统一成功响应语义
  - 统一关键错误语义

这意味着：

- Telegram 后续只需要作为轻入口调用现有统一接口
- Desktop 后续只需要作为桌面壳调用现有统一接口
- Chapter 2 不需要为了入口接入再建立新的业务中心或特殊后门
