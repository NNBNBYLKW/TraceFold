# Step 9 Chapter 3 First Wave Fixes

## Purpose

第一轮只修最影响系统感、最容易混淆层次与语义的项，不做大规模视觉或架构调整。

## Real Fixes Completed

### 1. Web detail semantics

- 详情页标题从 `Detail` 语义收拢到 `Pending Item / Expense Record / Knowledge Record / Health Record`
- `Source` 区块统一改为 `Source Reference`
- 来源字段标签统一改为 human-readable labels

### 2. Web fact / rule / AI layer wording

- `AI Summary` 统一收口为 `AI Derivation`
- AI 区块增加说明：它基于正式记录生成，不替代正式事实或规则提醒
- Rule alert 区块增加说明：它来自正式记录，但不替代正式记录

### 3. Workbench role wording

- Current Mode 区说明收紧为“只改变 entry context，不改变正式记录语义”

### 4. Telegram wording alignment

- capture / pending / dashboard / alerts / status 反馈统一到 `Pending item / Dashboard summary / Open rule alerts / Service status`
- pending detail labels 改为统一的首字母大写语法

### 5. Desktop shell wording alignment

- `Workbench: ...` 收口为 `Current mode: ...`
- `Service: ...` 收口为 `Service status: ...`
- service unavailable notification title 更明确

## What This First Wave Did Not Do

- 没有重做样式体系
- 没有重写页面布局
- 没有扩大 Telegram / Desktop 能力边界
- 没有改动业务语义或主链规则
