# TraceFold新对话入口

## 最新入口：发布准备收尾与主线整合

先读 [RELEASE-PREP-INTEGRATION-20261023.md](RELEASE-PREP-INTEGRATION-20261023.md) 和 CURRENT-STATE.md 顶部。
本轮已完成开发回归、真实产包及产品 smoke，并按用户要求把成果整合到本地 main。完整真实 UI 验收仍未完成；本轮按用户最新指令停在此处。
主目录的接续分支与未提交候选保持原样，主线整合检出位于 `.superpowers/release-prep-20261023`。后续不要误将两处工作树或受控与真实验证结果混为一谈。

## 迁移时入口（历史原文保留）

2026-10-23（Europe/London）。用户已退出WorkBuddy工作流；当前处于迁移后暂停点，尚未开始新的开发。

## 只读这几项开始

1. 根AGENTS.md：当前工作方式与安全边界。
2. CURRENT-STATE.md：准确代码恢复点和四项未闭残差。
3. LESSONS-LEARNED.md：过去反复出错的具体教训，按需读取。

本工作区：`T:/Windows/Documents/GitHub/TraceFold`。
分支：`codex/main-workspace-20261023`；整合基线HEAD：`1062ce881665c9c7f44f42f158b2e9f34d30f735`。

它是原主目录上的新接续分支，合入已核收开发提交链，并保留原main三项安全修复。19件Task13待完成改动逐件复制核对；Git仍有这些未提交改动、本次接续文档及原有0字节diff占位文件，不能以“整理干净”为由抹掉它们。原主目录既有忽略缓存、运行目录及bin/obj留在原位，不作为此次迁移输入或验证证据；以后运行使用全新输出叶。

## 新对话第一轮

只核对目录、HEAD/分支、git status与当前diff；说明已接受的工作范围和下一最小修复，随后等待用户继续。不要初始化即跑测试、编译、产品、主题恢复、进程清理或提交。不要再次向旧协调员或WorkBuddy索取卡片。

用户继续后，按根AGENTS.md中的六成员工作流执行：总负责人和助理形成方案，协调员派发及跟踪，执行者实现与回归，reviewer独立复核，攻坚者接手反复未解问题。共享源码保持单一写入owner，并行实现先分离写域；用户不再承担逐卡转发。

棘手问题尽早查找网上现有工具、框架或思路；连续两轮同类修复无核心推进就升级，不持续重复。攻坚者仍无法解决或工作流无法定位时，向总负责人报告并暂停该任务，由总负责人询问用户下一步。六名成员均已初始化；本轮仅授权工作流文档小任务；Task13及真实产品/UI开发仍暂停。

## 历史只在需要时查

- 回归主目录记录、迁移前index及23件来源备份：`T:/Windows/Documents/GitHub/TraceFold/.superpowers/codex-transition/20261023-return-main/`。
- 前一轮整理记录、原始源码快照、清理清单、项目记忆归档：`T:/TraceFold5CC/.superpowers/codex-transition/20261023/`。
- 原Task13候选及已有运行/审查：`T:/TraceFold5CC/.superpowers/development/T13-VISUAL-INT-01/`，冻结只读。
- 旧总负责人决定：`T:/TraceFold5CC/.superpowers/project-governance/`。旧执行卡不再产生权限；验收事实仍按各自范围保留。
- 原产品需求：`docs/superpowers/plans/2026-09-02-native-media-desktop-stage5cc-visual-uia-closure.md`，只读当前相关章节，不自动装入全史。

旧实际仓 `T:/TraceFold5CC` 仍在3285dc9且有13件改动；临时接续worktree `T:/TraceFold5CC/.superpowers/codex-workspace` 仍在e698861及原接续分支，均只读保留。原main分支仍指向e3e87e4；当前主目录已切换到上述接续分支，未推进main或push。
