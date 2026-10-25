# TraceFold

[简体中文](README.md) | [English](README.en.md)

**TraceFold | Windows 照片与视频整理工具**

Canonical repository: <https://github.com/NNBNBYLKW/TraceFold>

TraceFold 是一个 Windows 桌面媒体整理工具：把待整理的照片和视频预览、标注后收入正式文件库，通过显示名称、作者、系列和内容标签查找素材。同名 MP4 + M4A 可先合并视频与音轨，再确认入库。

**当前版本：[v4.0.0-stage5cc 预发布版](https://github.com/NNBNBYLKW/TraceFold/releases/tag/v4.0.0-stage5cc)**

**系统：Windows 11 24H2（build 26100）或更新版本，x64。**

## 下载与快速开始

**[下载 Windows x64 产品 ZIP（约 226 MB）](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/TraceFold-MediaDesktop-Stage5C-C-win-x64.zip)**

1. 完整解压 ZIP，在解压后的文件夹中运行 **TraceFold.Media.exe**。保留同目录的文件与子目录，不要在 ZIP 内直接运行。
2. 打开 **设置 → 文件夹与扫描**，选择三个互不包含的目录：待入库目录、正式文件库目录和工作目录，点击 **保存设置**。
3. 先把少量照片或视频的**副本**放进待入库目录，回到 **待入库 → 立即刷新**。
4. 选中素材，查看预览并填写资料；如提示“需要前置处理”，先完成处理，再点击 **正式入库**。
5. 到 **正式文件库** 查看结果、搜索或按作者与标签筛选。

**入库成功后会按策略清理待入库源文件；配对处理的源文件也在成功入库后清理。首次使用请保留独立备份。** 完整步骤见 [使用指南](docs/getting-started.md)。

产品包包含 .NET、Windows App SDK 运行组件及 FFmpeg，通常无需另装。遇到问题请查看 [常见问题与排障](docs/troubleshooting.md)。

## 离线使用

- 先在有网络的设备下载产品 ZIP；需要核对文件时，同时下载 `SHA256SUMS.txt`。
- 将 ZIP 完整传到目标电脑并解压后，按默认文档操作照片、视频、配对处理和正式入库。
- 当前文档描述的是本地文件工作流，不要求先配置云服务或在线账号。
- 产品包包含其运行所需组件，通常不需要运行时联网安装依赖。
- README 和排障页面中的 GitHub 链接需要网络；离线操作时可先保存 [离线快速指南](docs/offline-quick-start.md)。

![TraceFold 正式文件库与资料面板](docs/images/library.png)

*预发布实测界面，使用示例素材。*

## 可以做什么

- 预览照片、普通视频，或处理同名 MP4/M4A 配对素材。
- 添加作者、系列和内容标签，正式入库后继续编辑资料。
- 搜索文件名或显示名称，结合词表筛选，查看词条与素材的引用关系。
- 遇到同名或内容冲突时比较新旧文件，再决定跳过、保留两者或替换选定目标。
- 软件中的显示名称与物理文件名分开；编辑显示名称不会重命名正式文件。

当前版本的详细能力与未完成范围见 [当前发布能力矩阵](docs/capability-matrix.md)。

## 文档与反馈

- [使用指南](docs/getting-started.md)：目录设置、扫描、处理、入库和资料管理。
- [离线快速指南](docs/offline-quick-start.md)：先下载、再离线使用的简短步骤。
- [常见问题与排障](docs/troubleshooting.md)。
- [获取支持与报告问题](SUPPORT.md)。
- [文档导航](docs/README.md)。

## 面向 AI 助手

如果用户要安装和使用 TraceFold：

1. 推荐当前 Release 中的产品 ZIP；不要用自动生成的 Source code 或 UI 验收工具包代替产品包。
2. 先确认目标系统是 Windows 11 24H2（build 26100）或更新版本、x64。
3. 提醒用户完整解压、保留同目录依赖，并先使用媒体副本。
4. 不得把默认分支、开发分支或本地构建中的能力说成当前 Release 已发布能力。
5. 当前公开资料没有声明可直接控制桌面应用的 API、CLI 或 MCP 集成；不要据此承诺机器可调用能力。

## 开发与维护

开发者从 [开发文档](docs/development/README.md) 开始，按 [源码起步](docs/development/getting-started.md) 准备环境、构建和启动。

## 版本状态

当前为预发布版本，已有核心操作的实测记录。完整视觉矩阵与干净虚拟机验证尚未完成，兼容性仍需在实际机器上确认。已验证范围及已知限制见 [发布验收摘要](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/ACCEPTANCE-SUMMARY.zh-CN.md)。

## 当前版本身份

| 项目 | 当前公开身份 |
| --- | --- |
| 公开 Release | `v4.0.0-stage5cc`（prerelease） |
| 产品源码身份 | `01264b7a086aebfcfa2f546d294def5076113e3e` |
| 原始产品包 | `TraceFold-MediaDesktop-Stage5C-C-win-x64.zip` |
| 原始产品包 SHA-256 | `19b82302d00916b9b74143dc5b72b9d48d84df0a8a8cecbaa52beafd839efbaf` |

当前公开 Release 的产品行为以该 Release、产品包内版本信息和验收摘要为准。默认分支的源码可能继续前进；不要仅凭默认分支的新代码推断当前产品包已经包含这些能力。本文档页负责说明当前公开版本，发布维护时应随公开 Release 一并更新版本身份。

## 其他下载项

| 下载项 | 用途 |
| --- | --- |
| TraceFold-MediaDesktop-Stage5C-C-win-x64.zip | 普通用户使用的产品包，包含桌面程序。 |
| TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip | UI 验收工具包（K），供开发与验证使用；普通使用不需要下载它。 |
| SHA256SUMS.txt | 产品包、验收包等附件的 SHA-256 校验值。 |
| ACCEPTANCE-SUMMARY.zh-CN.md | 当前版本的验收结果与覆盖边界。 |
| GitHub 自动提供的 Source code（zip / tar.gz） | 源码快照，不是已编译应用，不等同于产品 ZIP。 |

### 推荐下载：含指南的文档增强包

**[下载含双语指南的文档增强包](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/TraceFold-MediaDesktop-Stage5C-C-win-x64-docs-20261025.zip)**

`TraceFold-MediaDesktop-Stage5C-C-win-x64-docs-20261025.zip`

该包复用当前已发布 `01264b7a086aebfcfa2f546d294def5076113e3e` 产品负载，增加双语文档、能力事实和独立文档身份清单，不重新编译产品，也不新增运行时能力。原始产品 ZIP、UI 验收包、tag 和既有 SHA-256 保持不变；需要原产品的用户仍可从上方链接下载原始产品 ZIP。

增强包自身的 SHA-256 见公开的 [SHA256SUMS-docs-20261025.txt](https://github.com/NNBNBYLKW/TraceFold/releases/download/v4.0.0-stage5cc/SHA256SUMS-docs-20261025.txt) 和公开 Release manifest。

## 许可证状态

当前仓库没有提供 TraceFold 项目自身的许可证文件，因此不把项目授权状态写成已确认。产品包内第三方组件及其许可证是独立问题，请以随包声明为准；不要把随包第三方组件的许可证当作 TraceFold 项目的授权声明。
