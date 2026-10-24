# 支持与反馈

先查看[快速开始](docs/getting-started.md)和[常见问题](docs/troubleshooting.md)。当前桌面版为 [v4.0.0-stage5cc 预发布](https://github.com/NNBNBYLKW/TraceFold/releases/tag/v4.0.0-stage5cc)。

## 提交问题

在 [Issues](https://github.com/NNBNBYLKW/TraceFold/issues) 搜索是否已有同类问题；没有的话，在[新建反馈](https://github.com/NNBNBYLKW/TraceFold/issues/new/choose)中选择“使用问题 / 故障”或“功能建议”。也可以选择空白 Issue，直接用自己的话描述。不必运行验收工具、编译源码或提供技术验收报告。

故障反馈尽量包含：

1. 使用的版本或下载附件名。程序能启动时，到“设置 → 系统诊断 → 重新检查”，在“日志与运行环境”查看产品版本和信息版本；不能启动时填写下载的 Release 标签即可。
2. Windows 版本与系统类型。`Win+R` 输入 `winver` 可查看版本和 build，Windows“设置 → 系统 → 系统信息”可查看系统类型。
3. 做了什么、希望发生什么、实际发生什么。用两三步说明即可；有错误文字时请原样保留错误代码。
4. 可选：问题发生时间、已尝试的方法，以及必要的截图或少量日志片段。无日志也可以反馈。

日志默认在 `%LOCALAPPDATA%\TraceFold\MediaDesktop\logs`；也可在系统诊断的“日志与运行环境”条目里点“打开日志目录”。自定义数据根时以诊断显示的路径为准。更详细的排查见 [FAQ](docs/troubleshooting.md)。

## 公开内容检查

本仓库的 Issue、评论和附件公开可见。发送前请检查截图、诊断摘要和日志片段，遮去不想公开的用户名、完整个人路径、文件名及其他个人信息。

不要上传本地数据库、私人照片/视频、账号凭据或访问令牌；通常用不含私人信息的示例名称和文字描述即可。需要最小复现时，优先使用你可以公开的示例素材。
