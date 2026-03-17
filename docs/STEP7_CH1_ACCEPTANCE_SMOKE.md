# STEP 7 CHAPTER 1 ACCEPTANCE SMOKE

## Purpose

本文档用于给 Step 7 / Chapter 1 提供最小验收收口。

Chapter 1 的目标不是实现 Telegram 或 Desktop，而是证明多入口后续可以复用同一条统一服务通路，而不需要特殊后门。

## What Was Added In Chapter 1

- `POST /api/capture`
- `POST /api/pending/{id}/confirm`
- `POST /api/pending/{id}/discard`
- `POST /api/pending/{id}/fix`
- 统一成功响应 envelope
- 统一关键错误 envelope

## What Existing Interfaces Were Reused

- `GET /api/pending`
- `GET /api/pending/{id}`
- `GET /api/dashboard`
- `GET /api/alerts`
- `GET /api/ping`
- `GET /api/healthz`

Chapter 1 没有新增 Telegram / Desktop 专用摘要接口。

## Minimal Smoke Scenarios

### Scenario 1: Capture To Pending

- 提交文本 capture
- 通过统一主链进入 pending
- 返回最小 capture / pending 标识

### Scenario 2: Capture To Formal Commit

- 提交高置信度文本 capture
- 通过统一主链直接进入正式事实层
- 返回最小 formal commit 摘要

### Scenario 3: Capture To Pending Detail To Confirm

- 提交 capture
- 通过 `GET /api/pending/{id}` 读取单条 action context
- 通过 `POST /api/pending/{id}/fix`
- 再通过 `POST /api/pending/{id}/confirm`
- 最终状态仍由统一 pending review service 决定

### Scenario 4: Lightweight Summary And Status Visibility

- `GET /api/dashboard` 提供 pending / alert 最小摘要
- `GET /api/alerts` 提供 health alert 最小可见性
- `GET /api/ping` 与 `GET /api/healthz` 提供最小服务状态可见性

### Scenario 5: Stable Error Consumption

- invalid capture input
- pending item not found
- pending item already resolved
- invalid fix input
- internal failure -> stable 500 envelope

## What Chapter 1 Intentionally Did Not Do

- 不实现 Telegram Bot
- 不实现 Desktop Shell
- 不新增 Telegram / Desktop 专用接口
- 不暴露 `force_insert`
- 不做批量 review
- 不做多轮复杂 fix 流程
- 不新增第二套业务逻辑

## Why This Is Enough For Chapter 2 Telegram Entry Work

Chapter 2 的 Telegram 轻入口已经不需要再发明专用写通路。

当前已经具备：

- 统一 capture 写入口
- 统一 pending action 写入口
- 统一 pending / dashboard / alerts / system status 读取面
- 稳定成功响应语义
- 稳定关键错误语义

因此，Chapter 2 可以把重点放在 Telegram 作为轻入口如何调用这些统一接口，而不是重新定义业务主链。
