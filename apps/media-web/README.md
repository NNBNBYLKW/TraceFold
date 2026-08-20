# TraceFold Media Web

用于扫描、标注、前置处理、冲突确认和检索正式媒体文件的本地 Web 界面。

## 开发

需要 Node.js 20 或更新版本。API 默认建议运行在 `http://127.0.0.1:8100`。

```powershell
cd apps/media-web
npm install
$env:VITE_API_BASE_URL="http://127.0.0.1:8100/api"
npm run dev
```

`VITE_API_BASE_URL` 必须指向媒体 API 的 `/api` 根路径；未设置时使用同源 `/api`。

### Windows 一键源码测试

首次使用前需要完成 API 与前端依赖安装，并确保 `ffmpeg`、`ffprobe`、
Node.js 和 Microsoft Edge 可用：

```powershell
cd apps/media-api
python -m pip install -e ".[dev]"
cd ../media-web
npm install
```

之后可直接双击仓库根目录的 `启动源码测试版.cmd`。启动器会运行当前
工作区中的 API 与 Vite，随后以 Edge 应用窗口打开 TraceFold；关闭该
窗口后，启动器只会结束自己创建的 Edge、Vite 和 API 进程树。

源码测试数据、日志和独立 Edge 配置均保存在仓库根目录的
`.source-test`，不会读取或修改 `data/media` 及离线测试包中的数据。需要
重置时，可以在确认不再需要测试数据后删除 `.source-test`。启动失败时，
详细信息保存在 `.source-test/runtime/logs`。

只验证服务能否启动、不打开窗口时，可在仓库根目录运行：

```powershell
python scripts/launch_source_test_app.py --verify-only
```

## 验证

```powershell
npm test
npm run build
npx playwright install chromium
npm run e2e
```

单元测试使用 Vitest。构建命令同时执行 TypeScript 类型检查和 Vite 生产构建。端到端测试会自行启动隔离的 API（端口 8101）和 Web 服务（端口 3101），并在每次运行前重置且仅重置 `apps/media-web/.e2e-data` 下的 `inbox`、`library`、`work` 和 `media.sqlite3`。

端到端发布门包含 FFmpeg 处理，不能用跳过相关用例代替通过。真实媒体不得放入 `.e2e-data`。
