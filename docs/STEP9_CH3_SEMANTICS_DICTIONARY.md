# Step 9 Chapter 3 Semantics Dictionary

| 中文主叫法 | English Mapping | 定义 | 适用层 | 禁止混用项 |
| --- | --- | --- | --- | --- |
| 捕获输入 | Capture | 用户提交的原始输入 | Capture / entry surfaces | 正式记录、Pending |
| 解析结果 | Parse Result | Capture 进入主链后的解析产物 | service / pending upstream | 正式记录 |
| 待确认项 | Pending Item | 需要确认、修正或丢弃的审核中间层对象 | Pending list/detail/actions | 正式记录、历史日志 |
| 确认 | Confirm | 将 pending item 按统一确认语义推进到正式层 | Pending actions | 快速保存、自动通过 |
| 丢弃 | Discard | 明确拒绝该 pending item | Pending actions | 删除正式记录 |
| 修正 | Fix | 对单条 pending item 的最小文本修正 | Pending actions / Telegram | 多轮状态机、字段表单 |
| 强制写入 | Force Insert | 系统内部高风险兜底能力 | service / internal only | 轻入口能力、普通确认 |
| 正式记录 | Formal Record | 已进入正式事实层的稳定业务记录 | Expense / Knowledge / Health details | pending、alert、AI derivation |
| 规则提醒 | Rule Alert | 从正式记录派生出的规则提醒 | Health / dashboard / alerts | 正式事实、AI 结论 |
| 严重级别 | Severity | 规则提醒的风险等级 | alerts / dashboard | status、priority 以外的业务状态 |
| 状态 | Status | 对对象当前状态的简短表达 | pending / alerts / service | severity、availability |
| AI 派生 | AI Derivation | 基于正式记录生成的解释、摘要或建议 | Knowledge / Health pages | 正式事实、规则提醒 |
| AI 摘要 | AI Summary | AI derivation 的一种具体呈现 | derivation content | 正式结论 |
| 模板 | Template | 命名工作模式入口 | Workbench | 动作链、自动化脚本 |
| 固定快捷 | Shortcut | 高频固定入口上下文 | Workbench | 执行按钮、后台任务 |
| 最近上下文 | Recent Context | 继续工作入口 | Workbench | 历史日志、审计时间线 |
| 总览 | Dashboard Summary | 跨模块的总览层摘要 | Dashboard / Workbench | 当前模式、最近上下文 |
| 当前模式 | Active Mode | 当前生效的工作模式入口 | Workbench / Desktop shell | 正式业务状态 |
| 默认模式 | Default Mode | 默认进入的工作模式入口 | Workbench | 用户当前操作状态 |
| 服务不可用 | Service Unavailable | 统一 API 当前不可达 | Web / Desktop / Telegram | empty、not generated |
| 空状态 | Empty | 当前没有可展示的数据 | all surfaces | unavailable、error |
| 未生成 | Not Generated | 可生成内容尚未生成 | AI derivation | failed、unavailable |
| 不支持 | Unsupported | 当前对象或场景不支持该能力 | Web / Telegram | empty、error |
| 已禁用 | Disabled | 配置对象存在但当前不生效 | Template / Shortcut | unavailable、not configured |

## Fixed Wording Notes

- Pending 统一叫法为 `Pending Item`
- 正式业务对象统一叫法为 `Expense Record` / `Knowledge Record` / `Health Record`
- 规则层统一叫法为 `Rule Alert`
- AI 层统一叫法为 `AI Derivation`
- Source 区块统一叫法为 `Source Reference`
