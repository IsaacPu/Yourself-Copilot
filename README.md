<p align="center">
  <img src="icons/brand/companion.svg" width="80" alt="Yourself Copilot 陪伴标志" />
</p>

<h1 align="center">Yourself Copilot</h1>

<p align="center">每天看得见的任务与备忘。<br />A quiet desktop companion for tasks, notes, and images.</p>

Yourself Copilot 是一个轻量的 Windows 桌面工具：把今天要做的事、明天的安排、随手记下的想法和参考图片放在桌面上，打开电脑就能接着工作。

它从“把计划表贴在桌面上”的小需求开始，逐步加入任务管理、备忘、历史记录和图片管理。目前采用本地文件保存，无需账号或 API Key。

## 界面预览

<p align="center">
  <img src="docs/screenshots/desktop-card.png" width="440" alt="桌面任务卡片：日期分组、待办列表、随手记和快速添加任务" />
</p>

实际运行的桌面卡片，使用演示数据。暖白底色、鼠尾草绿与柔和圆角，让常驻桌面的内容保持清楚、安静。

## 可以做什么

- **安排今天、明天和以后**：按计划日期查看任务，也可以在工作台选择具体日期。之前未完成的任务会在今天单独显示，原计划日期保留。
- **完成与撤销**：勾选后移入当前分组的“已完成”，默认收起；展开后以灰色删除线显示。支持修改标题、调整日期和撤销移除。
- **随手记想法**：桌面卡片与工作台共用备忘内容，支持多行输入；可以把选中文字转成今天的任务，原备忘保留。
- **自动保存与历史**：文字停顿约 600 毫秒后保存，支持 `Ctrl+S`；查看分组不逐次写盘。内容历史只读，保存失败时保留输入并提示重试。
- **调整桌面卡片**：拖动标题栏移动，四角沿对角线缩放，四边沿单轴缩放；支持锁定、隐藏和恢复位置。
- **把图片放在桌面**：多图导入、拖入文件或粘贴图片；支持移动、等比例缩放、锁定和隐藏，不会更换系统壁纸。GIF 显示静态首帧。
- **从托盘随时打开**：关闭工作台后继续驻留托盘；可在设置中开启登录 Windows 后自动运行，以静默方式显示桌面卡片。

## 快速开始

### 环境

- 64 位 Windows 10 / 11，标准 Explorer 桌面。
- .NET Framework 4.x 的 64 位 C# 编译器和 WPF 组件；构建脚本使用 Windows 目录下的 `Microsoft.NET/Framework64/v4.0.30319`。
- PowerShell。无需 npm、NuGet 或额外安装系统字体。

### 下载、构建和运行

```powershell
git clone https://github.com/IsaacPu/Yourself-Copilot.git
cd Yourself-Copilot
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
& '.\Yourself Copilot.exe'
```

也可以在 GitHub 点击 **Code → Download ZIP**，解压到自己有写入权限的文件夹，再在该文件夹运行构建命令。仓库不包含预编译 EXE。

首次运行会创建空的任务和图片库，并打开工作台。可以先添加一项任务，再从工作台显示桌面卡片。双击托盘图标可重新打开工作台；完整退出请使用托盘菜单“退出 Yourself Copilot”。

请将 EXE 与两个 XAML 文件、`fonts/`、`icons/` 一起保留。不要直接安装到需要管理员权限才能写入的目录。程序运行时无需联网。

### 登录时显示

在工作台“设置”中开启“登录 Windows 后自动运行”，再选择是否显示桌面任务卡片。登录启动使用 `--startup`，不会主动弹出工作台或抢当前窗口焦点。移动程序文件夹后，请重新设置登录启动路径。

## 数据保存在哪里

数据保存在 **EXE 所在文件夹**，不上传云端：

| 路径 | 内容 |
| --- | --- |
| `workboard.json` | 任务、备忘、草稿、完成记录、可撤销移除记录和卡片设置 |
| `library.json` | 图片列表、位置、大小、锁定和显示状态 |
| `images/` | 导入图片的本地副本，移动原文件不影响已导入的图片 |
| `*.json.bak` | 上一次有效保存的备份 |
| `history/workboard/` | 按日期存放的内容历史快照 |
| `workboard-recovery-*.json`、`library-recovery-*.json` | 数据异常时保留的原始文件 |
| `运行日志.txt` | 运行与异常诊断记录 |

工作台展示最近 300 个历史快照，磁盘上的历史文件不会自动删除。备份或迁移时，先从托盘退出，再复制整个程序文件夹。

仓库的 `.gitignore` 采用根目录白名单，排除个人任务、图片、日志、历史、部署记录、备份和编译产物。公开的预览图使用演示内容。

## 技术结构

采用 C# / .NET Framework，结合 WinForms、WPF `ElementHost` 与 Windows 桌面窗口接口。构建入口是 `build.ps1`，界面资源从程序目录加载。

| 文件 | 职责 |
| --- | --- |
| `DesktopNote.cs`、`NoteContext.cs` | 程序入口、托盘、桌面连接与图片窗口 |
| `WorkboardStore.cs`、`LibraryStore.cs` | 本地持久化、恢复、历史与图片库 |
| `WorkboardWindow.cs`、`WorkboardView.xaml` | 常驻桌面任务卡片 |
| `WorkbenchWindow.cs` | 任务、备忘、历史与设置工作台 |
| `ManagerWindow.cs`、`CompactWindow.xaml` | 桌面图片管理 |
| `CornerResize.cs`、`StartupManager.cs` | 缩放几何与登录启动 |
| `AppTypography.cs`、`AppVisuals.cs` | 字体、图标与品牌资源 |
| `qa/` | 使用隔离目录的回归与专项检查 |

## 验证

在 Windows 上先完成构建，然后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\verify-daily-all.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\verify-save-feedback.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\verify-edge-resize.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\verify-round-corners.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\qa\verify-brand.ps1
```

检查会在被忽略的 `.qa/` 目录内创建独立数据和测试程序；图片流程使用自动生成的通用测试图。检查范围包括日期分组、完成与撤销、保存反馈、窗口缩放、原生圆角和图标资源。其余存储、恢复与字体检查脚本也位于 `qa/`。

## 当前范围与限制

- 当前是本地 Windows 桌面工具；没有 AI 推理、Codex / MCP 接入、账号或云端同步。“Copilot”表达陪伴工作这一产品方向。
- 桌面贴附依赖 Explorer，替代桌面外壳及不同 Windows 环境需要另行验证。
- 卡片圆角采用原生窗口裁剪；尚未穷尽验证多屏混合 DPI、所有输入法及真实注销后登录场景。
- 本地 JSON 与图片未加密；历史不会自动清理。自动化检查不能替代所有真实使用场景。

## 字体、图标与许可证

- **Noto Sans SC** 与 **LXGW WenKai（霞鹜文楷）**：随程序附带，按 SIL Open Font License 1.1 分发。完整授权和版权声明见各字体目录；来源与校验值见 [字体清单](fonts/font-assets-manifest.json)。
- **Microsoft Fluent UI System Icons**：使用原版 SVG，按 [MIT 许可证](icons/LICENSE) 分发，来源见 [图标清单](icons/manifest.json)。
- **陪伴标志**：根据 ImageGen 概念手工绘制的应用 SVG，来源和生成方式见 [品牌资源说明](icons/brand/README.md)。

项目自身代码暂未指定开源许可证；仓库公开不改变第三方资源各自的授权条款。
