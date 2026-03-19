# Step 9 Chapter 5 Phase 1 Non-Goals Review

| id | non_goal | why_not_now | drift_signal | consequence_if_ignored | still_out_of_scope | wording_to_freeze |
| --- | --- | --- | --- | --- | --- | --- |
| NG-001 | 万能平台化 | Phase 1 要先稳住个人数据统一工作台，而不是吸纳任意工作流 | 出现“既然已经有 workbench，就什么都放进去” | 产品中心失焦，主线被稀释 | yes | `Workbench is a control layer, not a universal container.` |
| NG-002 | 自由自动化脚本/工作流引擎 | Template 现在只定义工作模式入口 | Template/Shortcut 开始承载动作链、脚本、任务参数 | Workbench 漂移成自动化平台 | yes | `Templates do not define workflows or scripts in Phase 1.` |
| NG-003 | AI 直接主导正式数据 | 正式事实必须继续由统一主链和人工确认守住 | AI 自动确认、自动修正、自动写正式表 | 正式事实层可信度被破坏 | yes | `AI remains derivation-only and never becomes the formal fact authority.` |
| NG-004 | Knowledge 图谱中心化 | Knowledge 是统一工作台的一域，不是系统中心 | 用图谱视图重组产品 IA 或视觉中心 | 产品从统一工作台漂到图谱平台 | yes | `Knowledge serves the workbench; it does not recenter the product.` |
| NG-005 | Health 医疗工具化 | Health 当前是记录、提醒、回顾系统 | 文案出现诊断口吻或医疗结论暗示 | 风险边界模糊，角色失真 | yes | `Health is not a medical decision tool in Phase 1.` |
| NG-006 | Desktop 重业务客户端 | Desktop 当前只做 shell + system bridge | 开始出现桌面独立模板页、业务页、业务写路径 | 第二业务前端出现 | yes | `Desktop remains shell-only in Phase 1.` |
| NG-007 | Telegram 管理端 / 第二业务中心 | Telegram 当前只做轻入口 | 增加模板 CRUD、复杂模式切换、管理端命令集 | 第二业务中心和特例语义出现 | yes | `Telegram remains a lightweight adapter and not a management client.` |
| NG-008 | 为兼容外部工具重塑方向 | TraceFold 中心必须保持在自身本地工作台 | “先兼容某工具再说”成为默认路线 | 产品主轴外移 | yes | `External tools may integrate later, but they do not reshape the product center now.` |
| NG-009 | 多系统并列事实源 | 正式事实必须继续单中心进入 | localStorage、桌面本地写入、Bot 特例写入 | 事实层失真，恢复困难 | yes | `There is still one formal fact path and no second fact source.` |
| NG-010 | 多租户 / 复杂权限 / 分布式 | 当前阶段目标是单人本地优先底座 | 文档或代码开始默认 tenant、role、queue、service mesh | 工程规模失控 | yes | `Phase 1 does not expand into multi-tenant or distributed platform architecture.` |
| NG-011 | 插件系统 / MQ / cache / 微服务化 | 当前阶段不需要这些工程规模 | 脚本、注释、命名开始预留平台级扩张 | 非目标被默许为默认下一步 | yes | `These remain out of scope unless a later phase explicitly reopens them.` |
| NG-012 | 把完整 browser E2E / 企业级 observability 包装成当前必须项 | 当前阶段只需要最小可信验证与运行可见性 | 把未建立的大体系说成当前完成定义 | 伪完成口径出现 | yes | `Phase 1 uses minimum credible validation, not enterprise platform completeness.` |

## Review Conclusion

以上非目标在当前仓库和当前阶段下仍然全部成立，没有发现需要在 Phase 1 内重开定义的项。
