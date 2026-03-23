# STEP 7 ERROR AND FEEDBACK

## Purpose

本文档用于定义 Step 7 的最小错误与反馈规则。

本文档只负责以下内容：

- entry-side feedback rules
- state visibility rules
- service unavailable presentation rules

本文档不负责以下内容：

- 业务恢复逻辑
- 离线补丁路径
- 第二写入路径
- 入口层补救流程设计
- 通知系统的完整设计展开

## Contents

- Purpose
- Error Categories
- Feedback Principles
- Telegram Feedback Rules
- Desktop Shell Feedback Rules
- Service Unavailable Behavior
- Logging Notes

---

## Error Categories

Step 7 当前只定义最小错误分类，用于统一反馈口径，不展开具体错误码、异常类或恢复机制。

最小分类如下：

- invalid input
- unsupported action
- not found
- already resolved
- service unavailable
- unexpected internal error

这些分类用于入口反馈约束，不等同于最终代码实现层的异常建模。

## Feedback Principles

Step 7 的反馈原则如下：

1. concise
2. state-aware
3. no fake recovery advice
4. no hidden fallback logic
5. no entry-specific semantic invention
6. no temporary local storage as shadow fact source

工程化要求如下：

- 反馈必须简短、明确，直接说明当前状态
- 反馈应基于统一 service/API 返回的状态，而不是入口侧猜测
- 入口层不得伪造“你可以这样恢复”的业务建议
- 入口层不得隐藏本地兜底逻辑或私有重试路径
- 相同问题在不同入口只能有不同呈现方式，不能有不同业务含义
- 不允许使用本地临时文件、临时队列、临时缓存作为影子事实源

Step 7 的通知边界在本文件中只保留最小口径：

- 服务不可达：允许主动通知
- 高优先级 health alert：允许克制通知
- pending reminder：以状态可见为主，可有低频提醒

本文件不展开通知编排、通知频率策略或通知 UI 设计。

## Telegram Feedback Rules

Telegram 反馈应保持 short and explicit。

适用规则：

- 应明确说明 capture submit 是否成功
- 应明确说明 pending action 是否成功
- 应明确报告 invalid id / already resolved / unsupported action
- 服务不可达时，必须明确说明动作未完成
- 对复杂查看、复杂修正、批量处理需求，应明确提示当前能力不在 Telegram 范围内
- 不在 Telegram 内创建多步恢复流程
- 不在 Telegram 内伪造完整业务页面或补救工作流
- 不在 service unavailable 时假装动作已经完成

补充约束：

- Telegram 的反馈目标是轻量承接，不是替代主工作台
- Telegram 只反馈动作结果和必要状态，不扩张为会话式恢复中心
- Telegram 不得因为入口轻量而自行推断 formal-write、pending review 或 parse 结果

## Desktop Shell Feedback Rules

Desktop feedback is more status-oriented。

适用规则：

- Service availability must be visible
- Desktop shell may suggest open workbench / retry / check service status
- Desktop shell must not take over business recovery decisions
- Desktop shell must not silently write temporary facts
- 状态呈现应优先说明“服务是否可达”“动作是否被执行”
- 桌面壳可承接最小通知，但不承接完整业务修复流程
- 桌面壳不建立独立业务错误面板体系

补充约束：

- Desktop 的反馈重点是状态可见、唤起 Web、提示检查服务状态
- Desktop 不负责决定是否重试某个业务动作
- Desktop 不负责决定某条记录是否应被视为已处理
- Desktop 不允许通过本地缓存或临时文件偷偷记录“待补写事实”

## Service Unavailable Behavior

当统一 service / API 不可用时，Step 7 只要求最小一致行为。

当前应至少明确：

- no silent failure
- no offline DB write patch
- no hidden temporary file fallback
- no second write path
- allowed: visible state, retry hint, open workbench, check service status

工程化要求如下：

- Telegram 不继续伪执行本地逻辑
- Desktop 显示服务不可达或状态异常
- Web 明确呈现服务不可用状态
- 任一入口都不得把动作悄悄改成本地暂存成功
- 任一入口都不得引入离线写库补丁或隐藏临时文件回退
- 任一入口都不得生成第二写入路径
- 允许的反馈只有：状态可见、重试提示、打开 Web workbench、检查服务状态

Step 7 的通知范围仅包括：

- 服务不可达
- 高优先级 health alert
- 待处理 pending 提醒

通知原则仍然是：

- 以状态可见为主
- 主动 push 为辅
- 必须克制
- 不是自动化引擎
- 不是复杂提醒平台

## Logging Notes

Step 7 当前只要求最小日志原则，不展开具体日志格式、字段清单或实现方式。

最小原则如下：

- keep request correlation
- keep entry source traceability
- make failures searchable

工程化解释如下：

- 保留请求关联信息，便于追踪一次动作经过了哪条统一链路
- 保留入口来源信息，便于确认错误来自 Telegram、Desktop Shell 还是 Web
- 让失败可搜索、可定位、可复盘

日志的目标是帮助排查统一 service/API 路径问题，而不是给每个入口建立独立诊断中心。
