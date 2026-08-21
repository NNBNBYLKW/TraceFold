# TraceFold Media Desktop Stage 1 验收记录

## 结论

Stage 1 作为本地测试检查点验收通过，可以合并到 `main`。

用户于 2026-08-22 确认当前 Windows 主机手工测试通过。由于没有可用的虚拟机或 Windows Sandbox，本次未执行干净 Windows 11 x64 客体断网测试；用户已明确接受如实记录该限制并继续合并。本记录不能作为“已在无开发工具客体中验证”的证明。

## 构建与版本

- 实现提交：`0571c3915f1be8a762818150cc6e4e464ca41f41`
- 构建机系统：Windows 11 25H2，OS build `26200.8875`（注册表 `ProductName` 仍报告 `Windows 10 Pro`）
- .NET SDK：`10.0.300`
- 测试包：`TraceFold-MediaDesktop-Stage1-win-x64.zip`
- ZIP SHA-256：`5535327B8177B198712A9C55DA56A9A81AF4BAE20AE6BB32C0306AB29772A1BB`

## 自动验证

- Release 构建：0 个警告，0 个错误。
- Architecture tests：3/3 通过。
- Application tests：11/11 通过。
- Infrastructure tests：6/6 通过。
- Desktop tests：12/12 通过。
- 总计：32/32 通过。
- 包内容检查：无 PDB；未包含 Python、Uvicorn、Node.js、Edge 或 Chrome 可执行文件。
- 主机烟雾测试：

```text
PASS: native window, single instance, offline startup, and clean exit verified
```

烟雾测试还验证了非法数据目录配置会以非零退出码结束，不会留下无窗口主进程；若同一包已有实例运行，脚本会拒绝执行且不会终止该实例。

## 手工验证

- 当前构建主机：用户确认通过。
- 原生 WinUI 独立窗口：通过。
- 三个文件夹的选择、保存及重启回显：通过。
- 重复启动激活已有实例：通过。
- 关闭窗口后进程退出：通过。
- 干净 Windows 11 x64 客体、断网、无开发工具环境：未执行（无可用虚拟机或 Windows Sandbox）。
- 干净客体 OS build：不适用。
- 干净客体已安装运行时证据：不适用；当前测试主机是包含 .NET SDK `10.0.300` 的开发环境。

## Stage 1 边界

Stage 1 是原生桌面基础检查点，数据库为空，仅保存基础设置；不包含 FFmpeg，也不执行媒体发现或入库。

Stage 1 does not scan, process, move, rename, or delete user media files.
