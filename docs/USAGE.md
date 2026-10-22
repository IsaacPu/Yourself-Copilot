# 使用与更新

## 第一次使用

从 [Releases](https://github.com/IsaacPu/Yourself-Copilot/releases/latest) 下载 `Yourself-Copilot-windows-x64.zip`，完整解压到有写入权限的文件夹，再运行 `Yourself Copilot.exe`。不要只从压缩包内打开 EXE，也不要把 EXE 单独移走；两个 XAML 文件、`fonts/` 和 `icons/` 都是运行资源。

首次打开是空的任务和图片库。添加任务时可选择今天、明天或其他日期；此前未完成的任务会在今天单列，原计划日期不变。完成的任务默认收起，展开后可恢复。

文字停顿后自动保存，`Ctrl+S` 可立即保存。保存失败时保留当前输入，请检查文件夹写入权限后重试。

## 桌面与托盘

拖动卡片标题栏移动，四边和四角均可缩放。锁定只限制移动和缩放，仍可编辑内容。图片通过工作台或托盘菜单添加，支持拖放和粘贴；GIF 仅显示首帧。

关闭工作台会收起到托盘。双击托盘图标可重新打开；退出程序请选择托盘菜单“退出 Yourself Copilot”。隐藏桌面内容不会删除任务和图片。

在设置中开启登录自动运行后，Windows 登录时会静默显示卡片，不主动弹出工作台。此选项会登记当前用户的 Windows 启动项；移动程序文件夹后需重新设置。

## 数据与备份

任务、备忘和图片保存于 EXE 同目录，没有云端同步，本地文件未加密。

| 路径 | 内容 |
| --- | --- |
| `workboard.json` | 任务、备忘、草稿、完成记录和卡片设置 |
| `library.json` | 图片列表、位置、大小、锁定和显示状态 |
| `images/` | 已导入图片的本地副本 |
| `*.json.bak` | 上一次有效保存的备份 |
| `history/workboard/` | 内容历史快照 |
| `*-recovery-*.json` | 数据异常时保留的原始文件 |
| `运行日志.txt` | 运行和异常诊断信息 |

历史界面显示最近 300 个快照，磁盘历史不会自动删除。备份时先退出程序，再复制整个程序文件夹。

## 更新版本

1. 从托盘退出程序，备份整个旧文件夹。
2. 将新 ZIP 解压到临时位置，再将其中的程序与资源复制到旧程序目录，按需覆盖同名程序文件。
3. 保留旧目录中的 `workboard.json*`、`library.json*`、`settings.json*`、`images/`、`history/` 和 recovery 文件，再启动程序。

官方 ZIP 不包含个人数据文件，不会用空白任务覆盖你的数据。若改在新文件夹运行，原目录数据不会自动迁入；请先退出，复制上述数据，再从新目录启动。更换路径后重新设置登录自动运行。

## 环境与反馈

需要 Windows 10 / 11 x64、.NET Framework 4.8 或以上及标准 Explorer 桌面。本次发布在 Windows、.NET Framework 4.8.1 环境验证；没有穷尽测试所有多屏 DPI、输入法或 Windows 配置。

当前版本未进行数字签名。Release 附有 `SHA256SUMS.txt`，可用 `Get-FileHash .\Yourself-Copilot-windows-x64.zip -Algorithm SHA256` 核对下载完整性。

遇到问题可在 [Issues](https://github.com/IsaacPu/Yourself-Copilot/issues) 提供版本号、Windows 版本和复现步骤。请先检查截图和日志中的个人内容。
