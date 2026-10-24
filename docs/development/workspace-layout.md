# 工作区布局

本文是当前路径与输出约定的参考。[源码起步](getting-started.md)、[测试](testing.md) 与 [发布](releasing.md) 提供对应操作步骤。

在仓库根开发，实际分支、HEAD 和工作树状态以 Git 为准。下表中的本地产物目录按任务创建；新克隆中没有这些目录不表示项目故障。

## 源码与文档

| 路径 | 当前用途 |
| --- | --- |
| apps/ | 应用及组件源码。桌面项目位于 apps/media-desktop/。 |
| scripts/ | 构建、启动、发布与验收脚本。 |
| packaging/ | Windows 测试包的打包模板与工具。 |
| docs/ | 用户、开发和维护文档；docs/superpowers、Stage 5C-C 与 phase1 的固定文档路径有实际消费者，保持其契约。 |
| apps/media-desktop/.packages/ | 项目内 NuGet 依赖目录；新克隆需恢复或自备离线源。 |
| 各项目 bin/obj/ | 构建生成物。 |
| apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap/ | 源码验收入口生成的 Support bootstrap。 |

## 交付物、结果与本地材料

以下均是本地输出或维护资料，不作为 fresh clone 的必备输入。Git 忽略规则不能替代保全和备份，也不表示某目录可任意删除。

| 路径 | 当前用途与规则 |
| --- | --- |
| dist/ | 本地构建的产品包、UI 验收工具包及展开物。已发布包以对应 Release 的附件和校验值为准。 |
| artifacts/evidence/ | 原始结果、截图、媒体、日志、源码绑定与首败；使用全新输出叶。 |
| artifacts/evidence/stage5cc-uia-gate/run-<guid>/ | 源码验收入口的默认结果目录。 |
| artifacts/reports/ | 汇总、发布回执与验证报告。 |
| artifacts/reports/publish-media-desktop/<guid>/ | publisher 的 TRX 结果，成功或失败均保留，不随发布 staging 清理。 |
| .cache/tmp/ | 运行时使用的短临时目录；按本次任务使用独立叶。 |
| .cache/downloads/ | 可校验恢复的工具输入，例如固定 winapp ZIP。 |
| .cache/ 中其他任务叶 | 可再生缓存及工具临时/staging；明确 owner 与恢复来源。 |
| .worktrees/ | 可选的隔离 Git worktree，用于分离开发任务；新克隆不自带。 |
| .source-test/ | API/Web 源码启动器的本地数据与运行目录，含 DB/profile；不是可整体清空的构建缓存。 |

publisher 在 dist 下使用自己的 staging/backup 事务目录。回滚未完成时，backup 具有恢复用途，不按普通临时缓存处理。个人资料、DB、完整用户 profile、唯一离线依赖或原始证据不归入可任意丢弃的缓存。

## 运行路径规则

- 实际最外层调用将 TEMP/TMP 设置为本工作区 .cache/tmp 下新建的短任务叶，并在 finally 恢复。不修改用户全局 shell、环境或 NuGet 配置。
- PS7、原生 PS5.1 与 dotnet 串行；运行期间冻结受测源码，以实际磁盘字节 SHA 绑定证据，重跑使用新持久输出叶并保留首败。
- 源码验收显式 -OutputDirectory 按实际 argv/cwd 解析，目标必须是全新叶。默认输出如上表。
- 便携工具包依据自身 toolkit.json 解析 support、driver、contract 和 resultsRoot，默认结果位于包内 results/；启动器按自身位置定位相邻产品或显式 -ProductDirectory。它不回退到源码、Git、SDK 或仓库缓存。
- 产品包排除 artifacts、.cache、.worktrees、.superpowers、验收 fixture 与私有数据；publisher 和独立结构 smoke 的检查包含 Hidden 属性项。

目录分类不改变用户产品安装布局、应用数据路径或媒体入库语义。
