# TraceFold Windows 离线测试版构建与验收

本文档面向构建者和测试者。该发行物是 Windows 10/11 x64 的离线功能测试包，不是正式安装器或生产备份方案。

## 构建与自动验证

在仓库根目录执行：

```powershell
python scripts/build_windows_test_package.py --version 0.1.0
py -3.12 scripts/build_windows_test_package.py --verify-only dist/TraceFold-Test-0.1.0-win-x64.zip
Get-FileHash dist/TraceFold-Test-0.1.0-win-x64.zip -Algorithm SHA256
```

构建脚本会先运行后端测试、前端测试和生产构建，再按白名单组装文件，下载 CPython 3.12 / Windows x64 wheels，生成哈希锁、包内清单、ZIP 和外部 SHA-256。自动验证会在仓库外的全新临时目录中完成以下检查：

- 外部 ZIP 校验值和包内 `MANIFEST.sha256`；
- ZIP 路径安全和唯一顶层目录；
- `PIP_NO_INDEX=1`、无效索引地址与安装命令中的 `--no-index`；
- Alembic 迁移写入包内 `data/media.sqlite3`；
- `/health`、前端首页和 `/api/settings`；
- 只结束验证器自己启动的 API 进程并确认端口释放；
- 写入待入库目录的标记文件后重启，确认文件和数据库保留。

## 测试电脑前置条件

- Windows 10/11 x64；
- Python 3.12 x64；
- 已加入 `PATH` 的 FFmpeg 和 FFprobe；
- Microsoft Edge；
- 安装和运行期间无需网络，也无需 Node.js。

## 人工验收

1. 校验 ZIP 的 SHA-256，断开网络，并解压到一个全新、可写的目录。
2. 双击 `安装测试环境.cmd`，确认成功；检查输出中包含 `--no-index`，且没有在线下载。
3. 双击 `启动 TraceFold.cmd`，确认打开无标签栏、无地址栏的 Edge 应用窗口，而不是普通浏览器窗口。
4. 将一张图片放入 `data/inbox`，刷新后添加作者、系列与内容标签并正式入库。
5. 将同名 MP4/M4A 放入 `data/inbox`，确认前置处理可合并，并在入库完成后不保留原始 MP4/M4A。
6. 分别制造目标冲突，验证跳过、两者保留和替换三个选择；弹窗中应展示预览并等待用户决定。
7. 关闭 Edge 应用窗口，执行以下命令确认端口已释放：

   ```powershell
   Test-NetConnection 127.0.0.1 -Port 8100
   ```

   预期 `TcpTestSucceeded : False`。

8. 再次启动，确认入库文件、标签、作者、系列、显示名称和处理记录仍然存在。

## 发行物检查

构建后可用下面的命令检查禁止内容：

```powershell
tar -tf dist/TraceFold-Test-0.1.0-win-x64.zip | Select-String -Pattern "node_modules|__pycache__|\.py[co]$|\.git/|\.env$|\.sqlite3$|test-results|playwright-report|\.e2e-data|data/.+\.(jpg|jpeg|png|mp4|m4a)$"
```

预期没有匹配。ZIP 内的 `data` 与 `runtime` 仅应包含用于保留目录的 `.gitkeep`。升级测试版时应解压到新目录，不覆盖旧目录；程序不会自动迁移或覆盖旧 `data`。
