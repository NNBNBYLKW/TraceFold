# 发布准备收尾与主线整合报告

日期：2026-10-23（Europe/London）。

**当前推进到：开发验证闭包、本地产包和真实产品 smoke 已完成；完整真实 UI 验收尚未完成。** 用户最新要求为收尾当前小阶段、并入主线并报告，本轮因此不继续展开后续 UI 矩阵。

## 整合与工作区

- 原本地 `main`：`e3e87e4773f8efba9a4ea44be4ab9cf68dd55a00`。
- 开发起点：`1062ce881665c9c7f44f42f158b2e9f34d30f735`，已包含原 main 安全修复与已核收开发链；本轮复制了原工作区28件候选/接续文件后在独立分支继续，未把这些既有内容冒充全新开发。
- 实现分支：`codex/release-prep-20261023`。成果连同本报告以快进方式整合到本地 `main`；源分支保留，未 push、打标签或对外发布。
- 操作检出：`T:/Windows/Documents/GitHub/TraceFold/.superpowers/release-prep-20261023`。
- 原主目录仍在 `codex/main-workspace-20261023`、HEAD `1062ce8`；28件原文件逐项 SHA-256 核对未变，未 reset、stash、清理或覆盖其未提交内容。原有工作流规则保留，本轮专用分支规则不推广到 main。

## 做了什么

| 实现提交 | 内容 |
|---|---|
| `36ffcf8` | 纳入继承的 Task13 候选并补足整链验证：真实 ledger 清理接线、实际整体移动包、首败/成功证据保留、PS5.1 数组形状和长路径夹具修复。核收 startup adapter 匹配及隔离 bootstrap，补齐隔离入口必读的 driver manifest；补强 stdin 超时后的退出与双流收口断言。 |
| `36ffcf8` | 单实例场景关闭前核对 PID、出生时间和映像，持有同一 Process 观察退出/退出码；重启实例也须正常关闭。发布增加 Support 的 win-x64 自包含 restore、每轮独立 TRX、短 staging 名和对应事务校验。 |
| `7977c6d` | 运行环境诊断原来仅接受 5C-B；增加 5C-C 与其精确信息版本前缀，同时保留提交号、程序集身份、未知阶段拒绝规则。 |
| `8ce837a` | 补齐产包器实际输出的 `ProductLaunchProbe` 字段；仅在 5C-C 接受字符串 NotRun/Passed。继续拒绝未知字段、错误类型、错误大小写及跨阶段身份。 |

真实产包曾两次被 `PackagedEnvironmentHealthy` 拦下：第一次暴露阶段不支持，第二次暴露清单新增字段与严格反序列化模型不一致。两处均以红/绿测试确认；最后完整真实产包成功，未跳过或放宽 smoke 门槛。首败日志仍保留。

## 验证结果与范围

| 验证 | 结果 | 范围 |
|---|---|---|
| PowerShell 7.6.5 完整 toolkit All | 395/395，exit 0 | 包括真实公开入口到70/Runner/Evidence的受控链；不等于真实UI通过 |
| Windows PowerShell 5.1.26100.9711 完整 toolkit All | 395/395，exit 0 | 同上，运行前后受测源码哈希未变 |
| 清单匹配针对性测试 | 32/32 | 两阶段正例、产包字段正例、跨阶段/错误类型/未知值反例 |
| 最终发布内 .NET 回归 | 1260/1260，6份TRX | 使用既有 SDK/执行器，未升级 SDK |
| 真实产品 smoke | Passed | 原生窗口、重复启动/退出、无新增浏览器；5A批处理、5B库命名/撤销、5C分类维护、5CB诊断等既有真实探针 |
| 最终工具包 PS5.1 ValidateOnly | Passed | 晋升后的实际包：521成员、7声明fixture；完整性/路径/身份检查，不启动产品 |
| 本轮产品/媒体进程与原生smoke根清理观察 | 无本工作区所属 TraceFold/ffmpeg/ffprobe 进程，无残留原生smoke根 | 仅声明本轮拥有对象，不声称全机没有其它用户进程 |

受控整链每个宿主均覆盖 Source/移动Package × Both/CurrentOnly：Both 为 Passed/exit0、94条索引；CurrentOnly 为 NotRun/exit1、47条。所有条目明确 Synthetic，journal 已关闭，真实 ledger 经末端替身清理，移动包前后哈希与实际运行路径可核对。

独立复核覆盖过 Task13 整链、61身份/退出及发布事务/RID/TRX，无阻断项；最终运行清单修复以针对性红绿测试和真实完整发布验证。Git blob 与磁盘 CRLF/LF 口径分开记录，不反写历史运行 hash。

## 本地产物

两份产物绑定代码提交 `8ce837a6909ef4f0b6f6f62360fad1d0817175f3`；后续接续与报告提交不改变产品源码。
信息版本：`4.0.0-stage5cc+8ce837a6909ef4f0b6f6f62360fad1d0817175f3`，产品清单 `ProductLaunchProbe=Passed`。

| 产物（位于本检出 dist） | 字节数 | SHA-256 |
|---|---:|---|
| `TraceFold-MediaDesktop-Stage5C-C-win-x64.zip` | 225872912 | `2B668B3015CBDB13E3032729F106C38E1412747E296760EC87508F599DF0E1F4` |
| `TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip` | 93051932 | `D1A63A17C397E0C3B013638FD7E7678BD0C1F1898CA2AF626B4954EB0E6E8C05` |

同名解压目录也已保留。ZIP、缓存、运行证据未加入 Git。

## 证据位置

相对本检出 `.superpowers/release-prep/`：

- `source-snapshot.json`：原工作区28件迁入文件的初始哈希。
- `toolkit-all-ps7-final-20261023-140627-6bce99/`、`toolkit-all-ps51-final-20261023-141814-ba31cf/`：完整双宿主日志、argv/cwd、源码快照及退出码。
- `publish-real-20261023-143137-187d55/`、`publish-real-fixed-20261023-174802-0f8beb/`：两次真实产包失败，保持原样。
- `manifest-5cc-red-20261023-174621-6df982/`、`manifest-5cc-green-20261023-174728-1dbbc1/`、`manifest-publisher-schema-red-20261023-175522-4c6bfe/`、`manifest-publisher-schema-green-20261023-175654-9611fa/`：清单兼容性红绿结果。
- `publish-real-schema-fixed-20261023-175718-5d026c/`：最终真实发布日志及源码绑定。
- `final-toolkit-validation-ps51-20261023-180117-ba8abd/`：最终包入口校验。
- `final-release-facts.json`：产物哈希、版本、1260测试汇总与拥有对象清理观察。
- 最终TRX在本检出 `.superpowers/publish-media-desktop/07e8893f262548a888736c5302cf82d2/`。

## 明确停点与下一步

- Task13 **开发残差**已闭合；真实产品/driver的明暗主题、布局、键鼠与完整截图矩阵未验收。
- Task12/61 的身份/关闭代码与开发回归已完成；正式真实 `SingleInstance` 场景仍未运行，产品 smoke 的有限单实例验证不替代它。
- Task14 的本地产包、分包、发布回归和真实产品 smoke 已完成；真实 `Scenario All -ThemeMatrix Both`、截图终审及最终自动验收记录仍未完成。
- 未做 clean VM，未改变用户拥有的人工验收行，未宣告最终 Stage5CC 发布通过。

后续应从本次已绑定源码的产物开始真实 StartupLayout/SingleInstance，再执行 All/Both、截图审阅、主题恢复和拥有对象清理核对；修改产品或工具包源码后必须重新产包并保留首次失败证据。
