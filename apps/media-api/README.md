# TraceFold Media API

本地媒体入库服务，负责扫描待入库目录、调度可扩展前置处理器、保存元数据、处理文件冲突并维护正式文件库。

## 环境与安装

需要 Python 3.12、FFmpeg 和 FFprobe。`ffmpeg`、`ffprobe` 必须能从 `PATH` 直接执行；缺少它们时，图片和普通媒体仍可管理，但 MP4/M4A 合并不能通过发布验收。

```powershell
cd apps/media-api
python -m venv .venv
.venv\Scripts\Activate.ps1
python -m pip install -e ".[dev]"
Copy-Item .env.example .env
alembic upgrade head
uvicorn media_api.main:create_app --factory --host 127.0.0.1 --port 8100
```

在 Bash 中激活环境可使用 `source .venv/bin/activate`。Web 应用使用其他端口或来源时，在 `.env` 中增加 `TRACEFOLD_MEDIA_WEB_ORIGIN=http://127.0.0.1:3100`。

## 运行路径

四个启动参数都按当前工作目录解析为绝对路径：

| 环境变量 | 用途 | 默认值 |
| --- | --- | --- |
| `TRACEFOLD_MEDIA_INBOX_DIR` | 下载完成后放置图片、视频及伴随音频的待入库目录 | `data/media/inbox` |
| `TRACEFOLD_MEDIA_LIBRARY_DIR` | 正式文件目录 | `data/media/library` |
| `TRACEFOLD_MEDIA_WORK_DIR` | 处理输出、预览、暂存和恢复数据 | `data/media/work` |
| `TRACEFOLD_MEDIA_DATABASE_PATH` | SQLite 元数据及操作日志 | `data/media/media.sqlite3` |

待入库、正式库和工作目录不能相同或互相包含；数据库不能位于待入库目录。前三个目录可在 Web 设置页修改，数据库位置只在启动时配置。

## 数据库与测试

每次更新代码后先执行迁移：

```powershell
alembic upgrade head
python -m pytest tests -q
```

健康检查为 `GET /health`，运行诊断为 `GET /api/system/status`。不要把 `apps/media-web/.e2e-data` 配置为真实媒体目录；它只用于可清理的自动验收数据。
