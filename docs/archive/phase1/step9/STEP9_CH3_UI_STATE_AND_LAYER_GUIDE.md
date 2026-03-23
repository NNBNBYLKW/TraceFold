# Step 9 Chapter 3 UI State And Layer Guide

## State Guide

| State | 含义 | 应表达什么 | 不应混成什么 |
| --- | --- | --- | --- |
| `loading` | 正在请求或生成 | 正在加载 / 正在生成 | empty、error |
| `empty` | 当前没有结果 | 当前无数据、无 open item | unavailable、not generated |
| `unavailable` | 服务或依赖不可达 | 服务暂不可用、稍后重试 | empty、disabled |
| `not configured` | 依赖或配置缺失 | 尚未配置 | unavailable、empty |
| `not generated` | 派生内容尚未生成 | 可生成但当前还没有 | failed、unsupported |
| `error` | 请求或生成失败 | 失败了、可重试 | unavailable、empty |
| `disabled` | 对象存在但当前停用 | 已禁用 | unavailable、deleted |

## Layer Guide

| Layer | 展示原则 | 示例语言 |
| --- | --- | --- |
| 正式事实 | 最稳、最确定 | `Expense Record`, `Knowledge Record`, `Health Record` |
| 来源引用 | 明确指向来源，不抢主层 | `Source Reference`, `Source Capture ID` |
| 待确认中间层 | 明确是审核中间层 | `Pending Item`, `Confirm`, `Discard`, `Fix` |
| 规则提醒 | 明确由规则派生，不替代事实 | `Rule Alert` |
| AI 派生 | 明确为解释/摘要/建议 | `AI Derivation` |
| 错误 | 帮用户理解发生了什么 | `Service status unavailable.` |
| 空状态 | 帮用户知道当前没有什么 | `No open pending items.` |
| Skeleton / Not available | 真实记录当前能力边界 | `Current mode: not set` |

## Display Order Principle

1. 正式事实
2. 来源引用
3. 审核中间层
4. 规则提醒
5. AI 派生
6. 错误 / 空状态 / 不可用状态

AI 派生和规则提醒都不能在语言上伪装成正式事实。
