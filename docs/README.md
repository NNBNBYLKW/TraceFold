# TraceFold 文档

## 普通用户

TraceFold 是 Windows 本地媒体整理工具，提供待入库处理、正式文件库和词表分类。

1. [下载与快速开始](../README.md)：v4.0.0-stage5cc 预发布版，选择产品 ZIP。
2. [使用指南](getting-started.md)：解压启动、三个目录、扫描处理、入库与资料管理。
3. [常见问题与排障](troubleshooting.md)。
4. [支持与反馈](../SUPPORT.md)。

适用系统及已知限制见 [版本状态](../README.md#版本状态)。

## 开发者

从 [开发文档](development/README.md) 开始：

- [源码起步](development/getting-started.md)：环境、依赖、构建和启动。
- [工作区布局](development/workspace-layout.md)：源码、缓存、交付物与证据的当前位置。
- [测试与证据](development/testing.md)。
- [构建发布](development/releasing.md)。
- [桌面组件说明](../apps/media-desktop/README.md)。
- [API / Web 开发入口](DEVELOPMENT_ENTRYPOINTS.md)、[环境约定](ENV_CONVENTIONS.md)、[架构规则](ARCHITECTURE_RULES.md)。

## 产品验证

- [Stage 5C-C 视觉/UIA 指南](media-desktop-stage5cc-visual-uia-test-guide.md) 与 [验收记录](media-desktop-stage5cc-visual-uia-acceptance.md)。
- [已发布版本验收摘要](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/ACCEPTANCE-SUMMARY.zh-CN.md)。

测试报告与构建产物由本地运行生成，不属于新克隆必备文件；对应位置见 [工作区布局](development/workspace-layout.md)。

## 解释与历史参考

这些文档用于理解设计背景与对应日期的决定，不作为当前安装或开发步骤：

- [桌面端设计](superpowers/specs/)、[计划](superpowers/plans/)和[复核记录](superpowers/reviews/)。
- [阶段历史与归档导航](archive/README.md)。
- [技术路线总览](技术路线总览.txt)、[软件最终需求](软件最终需求.txt)。
