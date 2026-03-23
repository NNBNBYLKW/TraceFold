# Phase 1 Summary

## 1. TraceFold 在 Phase 1 的项目身份

TraceFold 在 Phase 1 的定位，是一个 local-first 的个人数据 workbench / 基础平台，而不是一次性做全的“大而全产品”。

这一阶段的重点是先把可长期扩展的底座收口清楚：

- 一个稳定的主链
- 一个共享的应用服务层中心
- 一个统一的 SQLite 真相源
- 多入口共享同一业务主线，而不是各自长成独立系统

Phase 1 现在已经完成并封箱；当前工作的重点不再是继续解释每个过程步骤，而是以已冻结的边界为基线继续后续阶段。

## 2. 主链与系统边界

Phase 1 冻结的主链是：

`Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation`

这条主链对应的边界在当前仓库中已经锁定：

- 应用服务层仍是唯一真实业务逻辑中心
- Router 保持薄层，Repository 不升级为业务中心
- Formal facts、pending、rule alerts、AI derivations 语义分离
- AI 只能处于 derivation / explanation 边界，不能确认、丢弃、强插或改写正式事实
- Web、Desktop、Telegram 都是 entry surface，不是第二业务中心
- Workbench / Template / Shortcut / Recent / Dashboard 的角色边界已冻结

## 3. Phase 1 原本要完成什么

Phase 1 的原始目标，不是把所有端和所有业务都做满，而是先完成这些基础目标：

- API 工程骨架
- 公共配置 / 日志 / 异常 / 响应底座
- SQLite 作为唯一真相源
- 共享主线上的最小业务闭环
- Web 作为主业务界面
- Desktop 作为 shell-level entry
- Telegram 作为 lightweight adapter
- Workbench 首页作为统一入口层，而不是第二业务中心

当前仓库现实与这个目标是一致的：`apps/api`、`apps/web`、`apps/desktop`、`apps/telegram` 都已经存在，但它们的职责仍然遵守上述分工。

## 4. Step 3 ~ Step 9 完成了什么

不再逐章复述过程文档，按收口后的意义总结如下：

- Step 3 到 Step 6 期间，仓库逐步建立了 API 骨架、公共层、SQLite 底座、域模型与共享服务中心，并把主链从“结构设想”推进到可运行系统。
- Step 7 证明了多入口可以接入同一主线而不分裂系统：Telegram 仍是 lightweight adapter，Desktop 仍是 shell，不出现第二业务中心。
- Step 8 把 Workbench home、入口层语义、以及 Template / Shortcut / Recent / Dashboard 的角色分离进一步固定下来。
- Step 9 完成了 closeout：系统级 acceptance、弱边界清理、交互语义对齐、最低稳定性 hardening、非目标 relock、以及 Phase 1 的完成定义与后续基线整理。

Step 7、Step 8、Step 9 的过程证据现已归档到 `docs/archive/phase1/`，继续保留为历史依据，但不再作为默认阅读入口。

## 5. 当前各条子线到什么程度

### Workbench

Workbench 现在是主业务界面与入口层，不是万能平台容器。当前已冻结的角色分工是：

- Template：命名工作模式
- Shortcut：高频固定入口
- Recent：继续工作入口
- Dashboard：摘要层

它们不能被随意合并，也不应被改写成自动化引擎、图谱中心或通用工作区。

### Telegram

Telegram 当前是 lightweight adapter。

它承担：

- capture 输入
- pending 最小查看与最小处理
- dashboard / alerts / status 轻量读取

它不承担：

- 管理端
- 模板/模式管理
- `force_insert`
- 多轮 review state machine
- 数据库直连或独立业务语义

### Desktop

Desktop 当前是 shell-level entry，不是桌面业务客户端。

它承担：

- 打开共享 Web Workbench
- 提供最小 shell lifecycle
- 提供最小 tray / open / hide / quit 行为
- 提供最小 service status 可见性

它不承担：

- 原生业务页面
- 独立 pending / facts / rules / AI 流程
- 第二业务中心

Post-Phase-1 A3 与 Desktop Round 2 提升的是 shell credibility，不是产品定位升级。

## 6. 当前已完成能力

按收口文档与当前仓库现实，可以把已完成能力概括为：

- 统一主链已经收口，Capture / Pending / Confirm / Formal Record 不再被多入口打散
- Formal read layer 已建立，正式对象与 pending / rules / AI 派生层保持区分
- Dashboard summary layer 已建立，但不替代 Workbench 入口角色
- Rule / AI minimum enhancement layer 已建立，且保持 derivation 边界
- Telegram 轻入口已建立，并受共享 HTTP API 约束
- Desktop shell entry 已建立，并保持 shell-only 边界
- Workbench home 入口层已建立，Template / Shortcut / Recent / Dashboard 角色已有契约保护
- Phase 1 closeout 所需的 acceptance、boundary relock、minimum stability、tech debt 记录都已形成

“已完成”在这里表示 Phase 1 目标闭环成立，不表示这些能力已经扩展成更重、更广或更企业化的版本。

## 7. 当前仍保留的技术债

当前仍明确保留、但被认为不阻塞 Phase 1 封箱的技术债包括：

- Desktop runtime 仍是 skeleton-level shell，而不是 fully hardened desktop product
- repo-style ensure / bootstrap 脚本不是正式 migration framework
- Web 验证仍以 build 与 contract / smoke 为主，不是浏览器 E2E 平台
- 旧服务里仍有 `datetime.utcnow()` 警告类历史债务
- rule alert 生命周期在用户可见层仍有少量表达清晰度缺口
- 多 app pytest collection 仍需要分组运行，而不是一个统一收口命令

这些都是可接受缺口，不应被误写成“已经完全解决”。

## 8. 当前锁定边界与非目标

Phase 1 关闭后，以下边界默认锁定：

- TraceFold 仍是 local-first personal data workbench，不是万能平台容器
- 应用服务层仍是业务中心
- Desktop 仍是 shell-only
- Telegram 仍是 lightweight adapter
- AI 不得进入 formal fact mutation
- Workbench 角色分层不得随意塌缩

默认非目标仍包括：

- automation engine
- graph-centered recentering
- desktop-heavy business client
- Telegram management client
- AI-led formal fact mutation
- multi-tenant / distributed expansion
- plugin platform / enterprise-scale platformization

如果后续任务要重新打开这些边界，必须明确说明，而不能默默漂移。

## 9. 最低启动 / 恢复 / 验证基线

后续维护最常用的运行与验证入口，以以下文档为准：

- `docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md`
  - API / Web / Desktop 的推荐启动顺序、命令与配置基线
- `docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md`
  - A3 entry-layer 变更后的最低验证集合
- `docs/POST_PHASE1_DESKTOP_R2_ACCEPTANCE.md`
  - Desktop Round 2 之后 shell runtime 的实际能力边界
- `docs/TELEGRAM_BOT_SETUP_GUIDE.md`
  - Telegram adapter 的搭建与使用边界

补充说明：

- 仓库中不存在 `docs/POST_PHASE1_A3_ACCEPTANCE.md`，因此不应把它当成现有基线引用
- A3 的恢复提示与失败信号说明仍保留在 `docs/archive/phase1/post_phase1_a3/` 中，作为补充历史依据

## 10. Phase 1 最终结论与后续基线

当前可以把 Phase 1 的最终结论概括为：

- Phase 1 已完成并封箱
- 主链、系统中心、入口边界、Workbench 角色边界都已冻结
- 当前应从“已确认基线”继续，而不是重新从过程文档里拼结论

以后继续工作时，推荐阅读顺序是：

1. 先读本文件，了解 Phase 1 的整体结论与真实边界
2. 再读 `docs/ARCHITECTURE_RULES.md` 与 `docs/ENV_CONVENTIONS.md`，确认仍然生效的实现规则
3. 按任务类型补读启动、验证、Desktop、Telegram 相关手册
4. 只有在需要追溯历史证据时，再进入 `docs/archive/phase1/`

这份文档现在是 Phase 1 的默认主入口；归档目录保留历史过程证据，但不再承担主入口职责。
