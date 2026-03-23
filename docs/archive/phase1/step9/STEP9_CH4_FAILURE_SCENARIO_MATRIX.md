# Step 9 Chapter 4 Failure Scenario Matrix

| id | scenario | affected_layer | user_visible_signal | current_behavior | desired_behavior | severity | fix_now_or_later |
| --- | --- | --- | --- | --- | --- | --- | --- |
| FS-001 | API 不可达 | Web / Desktop / Telegram | 明确 `service unavailable` | Desktop/Telegram 已明确；Web 现已统一为更清楚的 service unavailable wording | 继续保持共享语义 | high | fix_now |
| FS-002 | Desktop 无法连 API | Desktop shell | `Service status: unavailable` + short hint + minimal notification | 已可见；本章继续确认语义可信 | 保持 shell-only 提示，不伪装 ready | high | fix_now |
| FS-003 | Telegram token 缺失 | Telegram config | 配置加载即失败 | 已在配置层拒绝 | 保持不进入运行态 | high | already_fixed |
| FS-004 | Telegram API 不可达 | Telegram adapter | `Service status unavailable. Try again later.` | 已清楚可见 | 保持不伪装成功 | high | already_fixed |
| FS-005 | Web 请求失败 | Web | 错误面板 + subject message | 本章前仍可能退化为浏览器或 JSON 错误；本章已收口成 user-facing wording | 区分 unavailable / invalid response / request failed | high | fix_now |
| FS-006 | Pending 动作失败 | API / Web / Telegram | 稳定错误语义 | 已有稳定 API envelope，Telegram 已有清楚反馈 | 保持可重试，不污染正式事实 | high | already_fixed |
| FS-007 | AI derivation 未生成 | Web | 明确 `not generated yet` | 本章前 wording 偏含混；本章已收紧 | 与 failed / unavailable 区分 | medium | fix_now |
| FS-008 | AI derivation 生成失败 | Web | 错误文案 + rerun CTA | 当前已可见 | 保持 formal record 可读 | medium | already_fixed |
| FS-009 | rule alert 暂无结果 | Web | empty-state | 当前已存在 empty-state | 保持不与 fetch error 混层 | medium | already_fixed |
| FS-010 | rule alert 获取失败 | Web | error-state | 当前已可见，但没有独立 rule-engine 失败语义 | 当前 Phase 1 可接受 | medium | later |
| FS-011 | 配置缺失/非法 | Desktop / Telegram | 配置层拒绝，错误明确 | 已在 Step 9 CH2 收紧 | 保持早失败 | high | already_fixed |
| FS-012 | 服务状态未知 | Desktop | `Service status: unknown` | 当前可表达 unknown | 保持不冒充 available | medium | already_fixed |
| FS-013 | acceptance runner 某一子阶段失败 | scripts | stage name + exit code | 本章前只看 return code；本章已增加 stage failure line | 让失败可定位 | high | fix_now |
| FS-014 | repo-style ensure script 被误读成正式 migration system | scripts / docs | role wording | 之前易被误读；本章继续保留明确说明 | 保持真实口径 | medium | already_fixed |

## Notes

- `rule alert 暂无结果` 与 `rule alert 获取失败` 当前已经在页面层分开，但还没有专门的“rule engine did not run”状态。该缺口保留为 Phase 1 可接受技术债。
- Desktop shell `open_workbench()` 的 `ready` 仍是 shell-level ready，不是 full browser/application ready。这一点必须继续按真实状态描述。
