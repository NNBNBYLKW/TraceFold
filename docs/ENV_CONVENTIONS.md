# 环境与配置约定

本页是当前生效的配置入口和维护规则。配置名、默认值及读取行为以对应源码为准；环境变量、脚本参数与持久化设置分别管理。

## 组件与读取入口

| 组件 | 当前变量 | 读取入口与示例 |
|---|---|---|
| 原生媒体桌面 `apps/media-desktop` | `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT` | [LocalAppDataPathProvider](../apps/media-desktop/src/TraceFold.Media.Infrastructure/Paths/LocalAppDataPathProvider.cs) |
| 共享 API `apps/api` | `TRACEFOLD_API_*`、`TRACEFOLD_AI_*` | [config.py](../apps/api/app/core/config.py)、[.env.example](../apps/api/.env.example) |
| Web `apps/web` | `VITE_API_BASE_URL` | [api.ts](../apps/web/src/api.ts)、[.env.example](../apps/web/.env.example) |
| Python 桌面壳 `apps/desktop` | `TRACEFOLD_DESKTOP_*` | [config.py](../apps/desktop/app/core/config.py)、[.env.example](../apps/desktop/.env.example) |
| Telegram adapter | `TRACEFOLD_TELEGRAM_*` | [config.py](../apps/telegram/app/core/config.py)、[.env.example](../apps/telegram/.env.example) |
| Feishu adapter | `TRACEFOLD_FEISHU_*` | [config.py](../apps/feishu/app/core/config.py)、[.env.example](../apps/feishu/.env.example) |
| 媒体 API `apps/media-api` | `TRACEFOLD_MEDIA_*` | [config.py](../apps/media-api/src/media_api/infrastructure/config.py)、[.env.example](../apps/media-api/.env.example) |
| 媒体 Web `apps/media-web` | `VITE_API_BASE_URL` | [.env.example](../apps/media-web/.env.example) |

各组件是独立配置上下文，不把共享 API/Web、Python 桌面壳或媒体 API 的变量当作原生媒体桌面的启动前置。

## 原生媒体桌面

`TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT` 可选，指定应用数据根的绝对路径。路径选择优先级为注入的 root override、进程环境变量、默认位置 `%LOCALAPPDATA%\TraceFold\MediaDesktop`；相对路径会被拒绝。

| 数据 | 默认根下的文件/目录 |
|---|---|
| SQLite 数据库 | `media-desktop.db` |
| 日志 | `logs`，文件 `tracefold-*.log` |
| 默认工作目录 | `work` |

原生应用直接读取进程环境，不使用 API 的 `.env` 加载器。待入库、正式库、工作目录由“设置 → 文件夹与扫描”保存；它们不是待新增的一组环境变量。`TRACEFOLD_DESKTOP_*` 属于 Python 桌面壳，不配置原生媒体桌面。

自动化若需要自定义应用数据根，必须使用专用隔离目录。用户数据库、profile 和媒体资料不作为临时缓存；不以换数据根或删除资料代替故障定位。

## API 配置合同

API 唯一读取入口是 `apps/api/app/core/config.py`。其他模块导入 `settings` 或调用 `get_settings()`，业务层不得直接调用 `os.getenv()`、`os.environ[...]` 或 `os.environ.get(...)` 重新解释配置。

API 使用该 app 的 `.env`、UTF-8 编码与大小写敏感的变量名。配置字段都有默认值；是否设置环境变量取决于运行环境，不能把文档中的示例值当成强制必填。

| 变量 | 源码默认值 | 用途 |
|---|---|---|
| `TRACEFOLD_API_APP_NAME` | `TraceFold API` | 应用名称 |
| `TRACEFOLD_API_ENV` | `development` | `development` / `test` / `production` |
| `TRACEFOLD_API_DEBUG` | `false` | 调试模式 |
| `TRACEFOLD_API_HOST` | `127.0.0.1` | 本地监听配置 |
| `TRACEFOLD_API_PORT` | `8000` | 端口 |
| `TRACEFOLD_API_DB_URL` | `sqlite:///../../data/tracefold.db` | 数据库连接，按敏感配置处理 |
| `TRACEFOLD_API_LOG_LEVEL` | `INFO` | 日志级别 |
| `TRACEFOLD_API_ENABLE_DOCS` | `true` | OpenAPI 文档开关 |
| `TRACEFOLD_AI_PROVIDER` | `openai_compatible` | API 服务端 provider |
| `TRACEFOLD_AI_BASE_URL` | `http://127.0.0.1:11434/v1` | provider 地址 |
| `TRACEFOLD_AI_API_KEY` | 空字符串 | 凭据；服务端专用 |
| `TRACEFOLD_AI_MODEL` | `qwen3.5:9b` | provider 模型 |
| `TRACEFOLD_AI_TIMEOUT_SECONDS` | `60` | 超时秒数 |

`production` 配合 `TRACEFOLD_API_DEBUG=true` 会被配置校验拒绝。示例文件可显式开启开发调试，这不改变源码默认关闭的事实。数据库路径及 API/Web 启动命令见 [开发入口](DEVELOPMENT_ENTRYPOINTS.md)。

## 其他应用

- 共享 Web 的实际公开入口是 `VITE_API_BASE_URL`，示例指向 `http://127.0.0.1:8000`；媒体 Web 的示例指向 `http://127.0.0.1:8100/api`。前端只能读取公开配置。
- Python 桌面壳读取 `TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL`、`TRACEFOLD_DESKTOP_API_BASE_URL`、`TRACEFOLD_DESKTOP_STARTUP_MODE`、`TRACEFOLD_DESKTOP_DEBUG` 和 `TRACEFOLD_DESKTOP_LOG_ENABLED`。前两个 URL 必填；启动模式为 `window` 或 `tray`。
- Telegram 读取 `TRACEFOLD_TELEGRAM_BOT_TOKEN`、`TRACEFOLD_TELEGRAM_API_BASE_URL`、`TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS`、`TRACEFOLD_TELEGRAM_DEBUG` 和 `TRACEFOLD_TELEGRAM_LOG_ENABLED`；token 与 API URL 必填。
- Feishu 的 `TRACEFOLD_FEISHU_APP_ID`、`TRACEFOLD_FEISHU_APP_SECRET`、`TRACEFOLD_FEISHU_VERIFICATION_TOKEN` 和 `TRACEFOLD_FEISHU_API_BASE_URL` 必填；其余实际字段见对应配置入口与示例。
- 媒体 API 的 inbox/library/work/database、扫描间隔、Web origin/dist 配置以其 `BootstrapSettings` 为准；不套用原生桌面的数据目录规则。

## 原生 PowerShell 与临时目录

使用已验证的原生 PowerShell 7；受影响脚本同时覆盖系统 Windows PowerShell 5.1。不要修改全局 shell、用户环境或永久网络/TLS 设置。

从仓库根选择本轮 owned 临时叶，在真正调用目标命令的最外层设置 `TEMP/TMP` 并恢复。下面是外层包装示例；把所需的测试或发布命令放到 `try` 中：

~~~powershell
$repoRoot = (Resolve-Path -LiteralPath '.').Path
$nativePs7 = (Get-Command pwsh.exe -ErrorAction Stop).Source
$ownedTmp = Join-Path $repoRoot ('.cache/tmp/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($ownedTmp)
$priorTemp = $env:TEMP
$priorTmp = $env:TMP
try {
    $env:TEMP = $ownedTmp
    $env:TMP = $ownedTmp
    # 在这里执行所需命令；检查并记录实际退出码。
} finally {
    $env:TEMP = $priorTemp
    $env:TMP = $priorTmp
}
~~~

持久证据放 `artifacts/evidence`，报告和 TRX 放 `artifacts/reports`；下载与可再生临时输入放 `.cache`。项目 `apps/media-desktop/.packages`、常规 `bin/obj` 与便携工具包自己的 `resultsRoot` 保持各自契约，不统改到临时目录。

## 配置维护规则

每个 app 自有变量有明确命名空间、owner、是否必填、敏感性和实际读取入口。不得用无归属的 `PORT`、`HOST`、`TOKEN` 等字符串新增 app 配置；Vite 公开变量使用其真实 `VITE_*` 机制。

每个 app 只维护自己的 `.env.example`。新增、删除、重命名或改变语义时，同步修改对应配置入口、示例、本文和实际引用。`.env.example` 是示例，不替代源码与规范；不把预留设计写成已生效变量，也不把凭据、数据库连接或服务端专用配置暴露到前端。

当前功能入口、固定文件名、路径、参数和变量只记录现行状态。变更过程与旧版本说明写到明确标识的解释/历史文档，不混进当前操作清单。
