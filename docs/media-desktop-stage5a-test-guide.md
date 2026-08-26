# TraceFold Media Desktop Stage 5A 人工测试指南

本指南只用于已解压的 Stage 5A 离线 Windows x64 包。测试期间保持网络断开；所有文件均为复制件，且所有目录必须位于本轮新建的隔离根目录，不能使用个人待入库目录或正式媒体库。

## 一、准备

1. 先生成本轮专属的所有权令牌；必须使用系统临时目录，令牌必须保留在包解压目录和整个手工测试根目录名称中。不要复用旧目录或省略令牌：

   ```powershell
   $token = [Guid]::NewGuid().ToString('N')
   $packageRoot = Join-Path ([IO.Path]::GetTempPath()) "tracefold-media-desktop-stage5a-package-$token"
   $root = Join-Path ([IO.Path]::GetTempPath()) "tracefold-media-desktop-stage5a-manual-$token"
   $data = Join-Path $root 'data'
   $inbox = Join-Path $root 'inbox'
   $library = Join-Path $root 'library'
   $work = Join-Path $root 'work'
   $fixtures = Join-Path $root 'fixtures'
   $screenshots = Join-Path $root 'screenshots'
   New-Item -ItemType Directory -Force -Path $packageRoot,$data,$inbox,$library,$work,$fixtures,$screenshots
   ```

2. 解压 `TraceFold-MediaDesktop-Stage5A-win-x64.zip` 到 `$packageRoot`；该目录与 `$root` 都必须是本次系统临时目录下带 `$token` 的直接子目录。
3. 在 `$fixtures` 中准备下列文件。除明确标注的同内容冲突副本外，标注为“唯一内容”的文件都必须与每个其他唯一内容文件有不同字节内容；不能用已入库文件的复制件代替。`ordinary.mp4` 可由 `paired.mp4` 复制并改名；`photo-copy.jpg` 必须是 `photo.jpg` 的内容相同副本，用于产生内容冲突。

   ```text
   photo.jpg                 （图片 1）
   image-b.jpg               （图片 2，唯一内容）
   refresh-remove.jpg        （图片 3，唯一内容，仅用于第 4 项后删除）
   ordinary.mp4              （普通视频）
   paired.mp4                （同名媒体对的视频）
   paired.m4a                （同名媒体对的音频）
   photo-copy.jpg            （photo.jpg 的相同内容副本，仅用于冲突项）
   still-processing.mp4     （同名媒体对的视频，唯一内容，仅用于第 9 项；不要前置处理）
   still-processing.m4a     （同名媒体对的音频，唯一内容，仅用于第 9 项；不要前置处理）
   stop-current.jpg          （唯一内容，仅用于第 10 项的当前项）
   stop-later-01.jpg         （唯一内容，仅用于第 10 项的后续项）
   stop-later-02.jpg         （唯一内容，仅用于第 10 项的后续项）
   shutdown-current.jpg      （唯一内容，仅用于第 12 项的当前项）
   shutdown-later-01.jpg     （唯一内容，仅用于第 12 项的后续项）
   shutdown-later-02.jpg     （唯一内容，仅用于第 12 项的后续项）
   ```

4. 在网络断开时，从 `$packageRoot` 启动原生程序；不要从源码目录启动：

   ```powershell
   $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = $data
   .\TraceFold.Media.exe
   ```

5. 在“设置”中依次选择并保存 `$inbox`、`$library`、`$work`；自动扫描保持关闭。每项截图以 `01-` 至 `13-` 开头保存在 `$screenshots`。后续步骤中的复制都从 `$fixtures` 到 `$inbox`，且只选择该步骤点名的项目，保证可按顺序执行。

每一个项目都必须填写：`通过 / 不通过`、`观察到的消息`、`截图文件名`。未通过时保留源文件和截图，不要手工删除产品内部目录。

## 二、人工测试项目

### 1. 离线、原生窗口与单实例启动

1. 保持网络断开，启动 `TraceFold.Media.exe`，确认出现原生窗口且没有浏览器窗口或新浏览器进程。
2. 再次启动同一个 EXE。

预期：第一个进程继续运行，第二次启动重定向到已有窗口后退出；全程只有一个应用实例。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 2. 扫描测试文件

1. 将 `photo.jpg`、`image-b.jpg`、`refresh-remove.jpg`、`ordinary.mp4`、`paired.mp4`、`paired.m4a` 复制到 `$inbox`。不要在本项复制 `still-processing.mp4`/`.m4a` 或第 10、12 项的专用文件。
2. 在“待入库”点击“立即刷新”。

预期：两张图片和普通视频可入库；`paired.mp4`/`paired.m4a` 作为同名媒体对显示为需要前置处理的一个项目，而不是两个可独立入库的普通视频。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 3. 多选与清除选择

1. 验证单击选择一项。
2. 按住 Ctrl 单击另一项；再按住 Shift 单击较远的一项，验证范围选择。
3. 在当前筛选结果中按 Ctrl+A；按 Esc 清除选择。

预期：选择数量与卡片高亮一致；Ctrl+A 仅选择当前筛选结果；Esc 清除全部选择。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 4. 筛选、刷新与按 ID 保留选择

1. 选择两项，修改搜索文字或状态筛选。
2. 恢复筛选，选择 `refresh-remove.jpg` 和一个仍保留的项目；在资源管理器中从 `$inbox` 删除专用的 `refresh-remove.jpg` 实际源文件，再点击“立即刷新”。

预期：搜索或状态筛选变化会清除选择；刷新不会按卡片位置误选，仍存在项目的选择按其 ID 保留，已删除源对应的选择消失。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 5. 批量资料：添加、移除、替换

1. 选择至少两项，点击“批量编辑资料”。
2. 分别在“添加”和“移除”中为作者、内容标签操作；在“替换”中替换作者和内容标签，并替换系列。
3. 应用修改后逐项查看右侧资料和实际文件名。

预期：资料按所选操作写入；显示名称与物理实际文件名均未被批量资料操作改名。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 6. 替换的未启用字段与明确清空

1. 在“替换”页保持一个字段（例如“替换系列”）未勾选，修改其他字段并应用。
2. 重新打开批量资料，勾选该字段的“明确清空”，单独应用。

预期：未启用的替换字段保持原值；只有显式“明确清空”才清除对应字段，空白输入本身不代表清空。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 7. 原子资料更新的自动化证据

本项不是生产包中的手工并发或故障注入操作。确认界面没有并发或故障注入入口；原子性证据标记为“自动化证据”：`BatchMetadataPersistenceTests` 已覆盖陈旧版本、触发器回滚和 600 项批量资料持久化。

预期：没有为本清单新增生产故障 UI；自动化证据涵盖陈旧版本、触发器回滚和 600 项案例。

- 通过 / 不通过：自动化证据 / ________
- 观察到的消息：`BatchMetadataPersistenceTests`（陈旧版本、触发器回滚、600 项）
- 截图文件名：________

### 8. 通用前置处理与串行 FFmpeg

1. 选择一个普通项目和 `paired.mp4` 对应的需要处理项目，点击“开始前置处理”。
2. 观察批量摘要及项目进度，直至处理完成。

预期：只有同名媒体对进入队列；普通项目显示不需要处理；实际 FFmpeg 工作保持串行，不同时运行多个转换。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 9. 混合批量入库与冲突保留

1. 先单独将 `photo.jpg` 正式入库；将 `photo-copy.jpg`、`still-processing.mp4`、`still-processing.m4a` 从 `$fixtures` 复制到 `$inbox`，并确认 `image-b.jpg`、`ordinary.mp4` 与第 8 项已完成前置处理的 `paired.mp4` 仍在待入库列表。不要对 `still-processing` 同名媒体对点击“开始前置处理”。
2. 多选 `photo-copy.jpg`、`image-b.jpg`、`ordinary.mp4`、已完成的 `paired.mp4` 和仍在处理需求状态的 `still-processing` 同名媒体对，点击“批量入库”，核对预览后只在一个汇总确认对话框点击“确认入库”。

预期：进度安全串行；不会连续弹出冲突对话框；三个就绪项目 `image-b.jpg`、`ordinary.mp4`、已完成的 `paired.mp4` 成功后从 `$inbox` 删除；与 `photo.jpg` 内容相同的 `photo-copy.jpg` 保持冲突并留在 `$inbox`；仍需要处理的 `still-processing` 同名媒体对被跳过并保留在 `$inbox`，状态仍为需要前置处理，不得被入库或当作成功项。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 10. 当前项完成后停止

1. 将仅供本项的 `stop-current.jpg`、`stop-later-01.jpg`、`stop-later-02.jpg` 从 `$fixtures` 复制到 `$inbox`，点击“立即刷新”，只选择这三个内容唯一、尚未入库的项目并启动“批量入库”。
2. 当前项出现进度后点击“当前项完成后停止”。

预期：当前项安全完成；后续项目不启动并留在待入库列表，批量摘要显示未完成项。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 11. 只显示未完成项、焦点与筛选恢复

1. 在第 10 项结束后的批量摘要点击“只显示未完成项”。
2. 确认焦点回到第一个未完成项目；然后清除或改变搜索/状态筛选。

预期：只显示未完成项时焦点准确落在第一个未完成项目；清除或改变筛选后列表回到相应筛选结果，批量未完成筛选不残留。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 12. 运行中关闭与重启恢复

1. 将仅供本项的 `shutdown-current.jpg`、`shutdown-later-01.jpg`、`shutdown-later-02.jpg` 从 `$fixtures` 复制到 `$inbox`，点击“立即刷新”，只选择这三个内容唯一、尚未入库的项目，启动批量入库并在 `shutdown-current.jpg` 显示为当前运行项时正常关闭窗口。
2. 重新启动同一离线包，使用相同 `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT`，点击“立即刷新”。

预期：没有丢失源文件、重复资产、未知正式文件或无法解释的备份；应用恢复到可判断和可继续处理的状态。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

### 13. 退出后的残留进程

1. 退出应用。
2. 在 PowerShell 中执行：

   ```powershell
   Get-Process -Name 'TraceFold.Media','ffmpeg' -ErrorAction SilentlyContinue |
     Select-Object Id,ProcessName,Path
   ```

预期：没有 `TraceFold.Media` 进程，也没有包内 `tools\ffmpeg\ffmpeg.exe` 残留进程。

- 通过 / 不通过：________
- 观察到的消息：________
- 截图文件名：________

## 三、测试交付记录

- ZIP 绝对路径：________
- ZIP SHA-256：________
- `package-version.json` 的 `StageCheckpoint`：`5A`
- `package-version.json` 的 `SourceCommit`：________
- 自动化测试总数：________
- 手工测试结论：通过 / 不通过
- 阻塞项、观察到的消息与对应截图：________

未通过任一项时，不创建验收记录，也不宣布 Stage 5A 已验收；将本指南、截图、ZIP 哈希和 `package-version.json` 一并反馈。
