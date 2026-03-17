# STEP 7 REVIEW CHECKLIST

## Purpose

用于 Step 7 PR review 的快速边界检查清单。

目标不是解释路线，而是帮助 reviewer 快速判断：这个 PR 有没有把 Telegram、Desktop 或其他入口写成特殊系统。

## Contents

- Entry-Layer Checks
- Service-Path Checks
- Drift Checks

---

## Entry-Layer Checks

- Does the entry only collect input, show state, or trigger approved actions?
- Does the entry avoid owning business decisions?
- Does Telegram stay lightweight?
- Does Desktop stay shell-like instead of becoming a business UI?
- Does the entry avoid batch workflow, complex review flow, or heavy detail rendering outside Web?
- Does the entry avoid direct DB access, hidden local writes, or private fallback storage?

若任一答案为 `No`，应视为入口越权风险。

## Service-Path Checks

- Does the action still go through approved service/API path?
- Is the business-state transition still defined in service layer?
- Did the PR keep router thin?
- Did the PR keep repository free of business rules?
- Does the same action still produce the same semantics across entries?
- Did the PR avoid adding a second write path or entry-specific formal-write semantics?

若任一答案为 `No`，应视为统一链路破坏风险。

## Drift Checks

- Did this PR add entry-specific semantics?
- Did it expose Telegram `force_insert`?
- Did it add multi-turn fix state machine in Telegram?
- Did it add desktop-only business logic?
- Did it introduce hidden persistence or fallback storage?
- Did it turn a temporary workaround into a durable architecture path?
- Did it create a second workflow center in notification, shell, or bot layer?

若任一答案为 `Yes`，应优先判定为 Step 7 偏航风险，而不是把功能可用性视为通过理由。
