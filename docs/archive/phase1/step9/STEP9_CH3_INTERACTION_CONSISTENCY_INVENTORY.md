# Step 9 Chapter 3 Interaction Consistency Inventory

| id | area | current_wording_or_behavior | target_wording_or_behavior | why_confusing | severity | fix_now_or_later |
| --- | --- | --- | --- | --- | --- | --- |
| IC-001 | Web detail pages | `Pending Detail / Expense Detail / Knowledge Detail / Health Detail` | `Pending Item / Expense Record / Knowledge Record / Health Record` | 同一对象在列表和详情里像不同概念 | high | fix_now |
| IC-002 | Web source sections | `Source` + snake_case labels | `Source Reference` + human-readable labels | 来源引用像内部字段 dump，而不是统一系统语言 | high | fix_now |
| IC-003 | Web AI sections | `AI Summary` 文案过于宽泛 | `AI Derivation` + “does not replace formal facts or rule alerts” | 容易让 AI 结果像正式结论 | high | fix_now |
| IC-004 | Web rule alert sections | 规则提醒无额外层级说明 | 明确“derived from formal records” | 容易误读为正式事实的一部分 | medium | fix_now |
| IC-005 | Workbench current mode copy | 当前模式说明偏抽象 | 直接说明只改变 entry context | 首页角色理解成本偏高 | medium | fix_now |
| IC-006 | Telegram capture / pending wording | `Pending review item`, `pending #12`, lowercase detail labels | `Pending item`, consistent labels, shared status wording | Telegram 像另一套产品文案 | high | fix_now |
| IC-007 | Telegram summary/status wording | `Dashboard:`, `Alerts:`, `Status: ok.` | `Dashboard summary:`, `Open rule alerts:`, `Service status: ok.` | 与 Web / Desktop 的核心状态词不一致 | high | fix_now |
| IC-008 | Desktop shell labels | `Workbench: Home`, `Service: available` | `Current mode: Home`, `Service status: available` | 壳层状态词和 Web/Telegram 不对齐 | high | fix_now |
| IC-009 | Desktop notification title | `TraceFold unavailable` | `TraceFold service unavailable` | 不够明确是服务状态问题 | medium | fix_now |
| IC-010 | Web list/detail structural language | `Detail`, `Content`, `Source` 风格仍略不齐 | 同类页继续朝 `Record / Source Reference / Derivation` 收拢 | 仍有少量语法飘移 | low | later |

## Remaining Later Items

- 列表页与详情页的小标题语法仍可继续细收
- Web 各模块空状态文案仍可进一步分出 “无数据 / 未生成 / 不支持”
- 目前未引入完整 i18n，因此中英映射仍以工程文档为主
