POST_PHASE1_WEB_OPTIMIZATION_EXECUTION_PACK

1. 目的

本执行包用于把当前 Post-Phase-1 Web 前端优化 从“规划文档集合”转成“可按顺序落地的执行序列”。

它回答四件事：

1. 先做什么，后做什么
2. 每一步的输入、输出、验收点是什么
3. 每一步明确不能做什么
4. 整轮优化什么时候算真正收口

本执行包不是：

- 新的产品定义文档
- 新的前端平台规划
- 新的 AI 扩张计划
- 新的 Desktop / Telegram 产品路线

2. 执行前提

当前执行必须默认站在以下已冻结事实之上：

- TraceFold 已过 Phase 1，当前是 Post-Phase-1 restrained enhancement
- Web 主消费线已经成立
- 本轮前端工作不是重搭骨架，而是在既有正式消费基线上做 clarity / hierarchy / readability / consistency 强化
- formal facts 仍然 primary
- AI / alerts / runtime 都只能是 secondary 或 support layer
- Web 仍然只是主要业务界面，不是业务真逻辑中心

3. 本轮执行总边界

3.1 允许推进的范围

- Workbench / Dashboard
- Knowledge detail + knowledge_summary
- Health records + rule alerts
- 这三条线共用的 shared shell / shared state / hierarchy consistency

3.2 明确不做的方向

- AI center
- alerts center
- rule management console
- task runtime control center
- model / prompt configuration UI
- Web-owned business workflow layer
- Desktop-specific product semantics
- Telegram-specific product semantics
- design-system rewrite
- global state platform rewrite
- whole-site component overhaul

4. 总执行顺序

建议按四个波次推进。

Wave A：冻结优化口径

1. WEB_PP1_TASK1_FRONTEND_OPTIMIZATION_CHARTER
2. WEB_PP1_TASK2_INFORMATION_HIERARCHY_CONTRACT

Wave B：做横向收口

3. WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH
4. WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT

Wave C：做逐页 polish

5. WEB_PP1_TASK5_WORKBENCH_DASHBOARD_HIERARCHY_POLISH
6. WEB_PP1_TASK6_KNOWLEDGE_DETAIL_PRESENTATION_POLISH
7. WEB_PP1_TASK7_HEALTH_RECORDS_ALERTS_PRESENTATION_POLISH

Wave D：统一闭环

8. WEB_PP1_TASK8_CROSS_PAGE_CONSISTENCY_AND_SMOKE

5. 全局执行规则

整个执行过程中，所有任务都必须满足这 6 条。

5.1 formal-first

正式记录必须始终优先于 AI、alerts、runtime support。

5.2 hierarchy-before-capability

优先优化：

- layout
- emphasis
- section order
- state clarity
- action weight

而不是增加功能。

5.3 local-degradation

支持层失败要尽量局部降级，不轻易整页 collapse。

5.4 restrained actions

动作必须服务于理解后的继续操作，不能长成控制台。

5.5 no web-owned business logic

前端不能为“更顺手”开始承担业务真逻辑。

5.6 one-workbench feeling

所有变更最终都要服务于“一个统一工作台”的感觉，而不是三页各自优化。

6. Task 1 ~ Task 8 执行清单

Task 1
WEB_PP1_TASK1_FRONTEND_OPTIMIZATION_CHARTER

目的
冻结本轮 Web 优化的目标、范围、非目标、页面角色和执行顺序。

输入

- 当前 Web consumption baseline
- 当前项目 Post-Phase-1 边界

输出

- docs/WEB_POST_PHASE1_OPTIMIZATION_PLAN.md

实现重点

- 明确本轮是“已成立消费线的工作台强化”
- 明确不做 AI center / alerts center / design-system rewrite
- 明确只覆盖 Workbench / Knowledge / Health + shared state

验收

- 后续所有 Web 任务都能用它判断是否偏航
- 它描述的是优化方向，不是新产品定义

禁止事项

- 不重写产品定位
- 不把优化文档写成系统重新定义

Task 2
WEB_PP1_TASK2_INFORMATION_HIERARCHY_CONTRACT

目的
冻结跨页 hierarchy vocabulary：primary / secondary / contextual / action / state。

输入

- Task 1 charter
- 当前 Workbench / Knowledge / Health 正式边界

输出

- docs/WEB_INFORMATION_HIERARCHY_CONTRACT.md

实现重点

- Workbench = entry layer
- Dashboard = summary layer
- Knowledge = formal-first + AI-secondary
- Health = formal-first + alerts-secondary
- state local by default
- actions subordinate to understanding

验收

- 三条主线都能映射到同一套 hierarchy language
- formal facts never outranked by derived layers

禁止事项

- 不把 hierarchy contract 写成视觉设计宣言
- 不把 consistency 理解成所有页面长一样

Task 3
WEB_PP1_TASK3_SHARED_PAGE_SHELL_POLISH

目的
建立最小 shared presentation 壳层，统一 page / section / state / source / action 的结构规律。

输入

- Task 1
- Task 2
- 现有 Workbench / Knowledge / Health 页面

输出

- 轻量 shared shell 实现
- docs/WEB_SHARED_PAGE_SHELL_POLISH_BASELINE.md

建议最小壳层

- PageShell
- PageHeaderBlock
- SectionShell
- StateBlock
- SourceReferenceBlock
- SectionActionRow

验收

- 三条主线的 header / section / state / contextual / local action 规律更稳定
- formal vs derived / reminder 的壳层关系更清楚

禁止事项

- 不做 design-system rewrite
- 不做 full-site component overhaul
- 不顺手新增 Web-only workflow

Task 4
WEB_PP1_TASK4_SHARED_STATE_VISUAL_ALIGNMENT

目的
统一 shared state 的视觉与落点，重点处理 page-level vs section-level、formal failure vs support-layer failure 的区分。

输入

- Task 2 hierarchy contract
- Task 3 shared shell

输出

- 最小 state visual contract
- state block 视觉与位置对齐实现
- docs/WEB_SHARED_STATE_VISUAL_ALIGNMENT_BASELINE.md

必须对齐的语义

- loading
- empty
- unavailable
- degraded
- derivation states
- alert states

验收

- primary read failure 才 page-level unavailable
- secondary failure 只 section-level unavailable
- Knowledge derivation failure 不伪装成 formal detail failure
- Health alerts unavailable 不伪装成 formal record failure

禁止事项

- 不改状态定义
- 不新造 state framework
- 不把状态块做成新页面中心

Task 5
WEB_PP1_TASK5_WORKBENCH_DASHBOARD_HIERARCHY_POLISH

目的
把首页做得更像工作台入口与 summary layer，而不是平铺信息墙。

输入

- Task 3 shell
- Task 4 state alignment
- /api/workbench/home
- /api/dashboard
- /api/system/status

输出

- Workbench / Dashboard 首屏层级优化
- docs/WEB_WORKBENCH_DASHBOARD_HIERARCHY_POLISH_BASELINE.md

首屏优先级

1. 当前整体状态
2. 当前最值得看的摘要
3. 下一步进入点
4. 支持性说明 / 状态

验收

- Workbench 更像 entry layer
- Dashboard 更像 summary layer
- degraded 主要局部挂载
- 首页更快回答“现在怎样、接下来去哪”

禁止事项

- 不新增首页聚合 API
- 不把 Workbench 做成第二业务中心
- 不把 runtime status 做成 control center

Task 6
WEB_PP1_TASK6_KNOWLEDGE_DETAIL_PRESENTATION_POLISH

目的
强化 Knowledge detail 的 formal-first 展示，稳定 AI summary 的次级位置与 derivation state 呈现。

输入

- Task 3 shell
- Task 4 state alignment
- /api/knowledge/{id}
- /api/ai-derivations/knowledge/{id}
- recompute endpoint

输出

- Knowledge detail 展示优化
- docs/WEB_KNOWLEDGE_DETAIL_PRESENTATION_POLISH_BASELINE.md

固定分区顺序

1. 页面标题 / 最小上下文
2. Formal Content
3. Source Reference
4. AI-derived Summary
5. 局部 derivation action / state

验收

- Formal Content 仍是 primary
- AI-derived Summary 仍是 secondary
- ready / not generated / failed / invalidated / pending / unavailable 更好区分
- recompute 仍是小动作

禁止事项

- 不新增 provider / model / prompt controls
- 不新增 AI center
- 不让 AI 文本覆盖 formal truth 主位

Task 7
WEB_PP1_TASK7_HEALTH_RECORDS_ALERTS_PRESENTATION_POLISH

目的
强化 Health 页的 formal-first + alerts-secondary 结构，提升 lifecycle grouping 与 alerts states 可读性。

输入

- Task 3 shell
- Task 4 state alignment
- /api/health
- /api/health/{id}
- /api/alerts
- acknowledge / resolve endpoints

输出

- Health 展示优化
- docs/WEB_HEALTH_RECORDS_ALERTS_PRESENTATION_POLISH_BASELINE.md

固定分区顺序

1. 页面标题 / 最小上下文
2. Formal Record
3. Source Reference
4. Rule Alerts
5. 局部 alert actions / state

验收

- Formal Records 仍是 primary
- Rule Alerts 仍是 secondary reminder layer
- open / acknowledged / resolved / empty / unavailable 更清楚
- acknowledge / resolve 仍是小动作

禁止事项

- 不新增 health AI
- 不新增 medical assistant UI
- 不新增 alerts center 或 rule config surface

Task 8
WEB_PP1_TASK8_CROSS_PAGE_CONSISTENCY_AND_SMOKE

目的
把前三条页面线与 shared state 做统一收口，形成文档闭环、manual smoke 顺序和 automated verification 入口。

输入

- Task 1 ~ Task 7 产物
- 当前正式 Web baseline docs

输出

- docs/WEB_PP1_CROSS_PAGE_CONSISTENCY_AND_SMOKE_BASELINE.md
- 最终 manual smoke 清单
- 最终 automated verification 命令

统一检查四类一致性

1. 页面角色一致性
2. 状态语义一致性
3. 主次关系一致性
4. 动作重量一致性

建议自动化回归入口

powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py

禁止事项

- 不把 closure 变成整站 redesign
- 不顺手新增新中心或新 API
- 不把优化文档写成新的系统总定义

7. 推荐实施节奏

建议按下面方式推进，而不是 8 个任务同时散开。

Sprint A：冻结口径

- Task 1
- Task 2

完成标志

- 所有后续任务都有明确判断标准
- 团队不会在实现时重新解释页面角色

Sprint B：横向收口

- Task 3
- Task 4

完成标志

- 三条主线有共同 page/section/state 语义
- shared state 的 page-level / section-level 规则清楚

Sprint C：逐页 polish

- Task 5
- Task 6
- Task 7

完成标志

- Workbench 更像入口页
- Knowledge 更像 formal record + derived aid
- Health 更像 formal record + reminder layer

Sprint D：统一闭环

- Task 8

完成标志

- manual smoke 和 automated verification 形成正式出口
- 文档链条完整
- 页面更明显像“一个系统”

8. Codex 执行时的硬规则

如果这份执行包要直接交给 Codex 或实现助手，建议把下面这些写成硬规则：

1. 不要新增 API，除非当前任务文档明确要求
2. 不要把 shared shell 扩成 design system
3. 不要把任何页做成 AI / alerts / runtime control center
4. 不要让前端承担业务真逻辑
5. 不要让 formal facts 失去主位
6. 不要把本轮扩成全站 redesign
7. 每个任务完成后先跑对应 smoke / tests，再进入下一个任务

9. 整轮完成定义

这轮 Post-Phase-1 Web 优化，只有在同时满足下面几项时才算完成：

- Workbench / Dashboard、Knowledge、Health 三条主消费线都完成既定 polish
- shared shell 与 shared state 已收口
- formal / derived / reminder / support 的边界跨页一致
- 小动作没有长成控制台
- 文档链条完整
- manual smoke 可重复
- automated verification 可作为正式回归入口
- 没有出现 AI 平台化、alerts center 化、Web 业务中心化等偏航