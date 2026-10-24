# 开发文档

在仓库根目录进行开发。桌面端的环境、依赖和启动步骤见 [源码起步](getting-started.md)；实际分支和 HEAD 以 Git 为准。

| 需要做的事 | 文档 |
| --- | --- |
| 准备桌面开发环境、构建与启动 | [源码起步](getting-started.md) |
| 查源码、缓存、交付物与证据位置 | [工作区布局](workspace-layout.md) |
| 选择回归范围、运行测试、保存证据 | [测试与证据](testing.md) |
| 准备工具输入、构建产品与验收工具包 | [构建发布](releasing.md) |
| 查看桌面项目配置和组件 | [桌面组件说明](../../apps/media-desktop/README.md) |
| 开发 API / Web | [对应开发入口](../DEVELOPMENT_ENTRYPOINTS.md) 与 [环境约定](../ENV_CONVENTIONS.md) |

普通用户使用 [产品指南](../getting-started.md)，不需要执行开发命令。

新克隆只包含版本控制中的源码与文档。本机 artifacts、dist、.cache、.worktrees 及项目 .packages 可能不存在；按当前任务生成或准备，不将它们缺失视为源码损坏。
