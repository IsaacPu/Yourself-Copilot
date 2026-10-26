# 开发与发布

日常使用请下载 [Release EXE安装包](https://github.com/IsaacPu/Yourself-Copilot/releases/latest)。本文面向修改源码和构建程序的开发者。

## 目录

```text
src/       C#、XAML 和 Windows manifest
assets/    随附字体、图标及其许可证
scripts/   构建和打包入口
tests/     回归与专项检查
docs/      使用说明和界面预览
VERSION    当前公开版本号
dist/      本机构建与 ZIP 输出（不进入 Git）
```

采用 C# / .NET Framework、WinForms 与 WPF ElementHost。桌面贴附使用 Windows 窗口接口；数据位于运行程序的目录。无需 npm 或 NuGet。

## 构建

在 Windows x64 上准备 .NET Framework 4.x 编译器和 WPF 组件，再在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
& '.\dist\Yourself-Copilot\Yourself Copilot.exe'
```

构建结果包含EXE、XAML、字体、图标、插画和声音，可从 `dist/Yourself-Copilot/` 运行。不要只复制EXE。版本读取 `VERSION`，构建时写入EXE的文件版本与产品版本。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-daily-all.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-save-feedback.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-edge-resize.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-round-corners.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-brand.ps1
```

测试使用忽略的 `.qa/` 隔离目录与中性测试图片。其余脚本覆盖共享存储、恢复、窗口切换和字体等专项行为；自动化检查不等同于所有真实场景均已验证。

每次检查会新建隔离目录，旧目录不会自动删除。验收结束后先保存需要的报告、截图或复现脚本，再清理 `.qa/` 中对应的运行副本。正式源码和测试应保存在 `src/`、`tests/`，不要把 Git 仓库或个人数据放入 `.qa/`。`dist/` 可通过构建和打包重新生成；清理前确认其中没有运行后产生的个人数据。

## 生成下载包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-package.ps1
```

打包脚本重新构建，并用白名单将程序、运行资源和用户说明装入 `dist/Yourself-Copilot-windows-x64.zip`，同时生成 `dist/SHA256SUMS.txt`。不会递归打包整个项目或已经运行过的程序目录。包检查会独立解压并核对文件、版本、校验值与空数据首启。

ZIP作为可验证的中间载荷保留在本地。发布前核对文件、版本和校验值，并验收空数据首启，再使用[安装器构建说明](INSTALLER.md)生成EXE。Release标签使用 `v` 加 `VERSION`，面向用户只上传安装EXE，SHA256放入更新说明。

安装器采用Inno Setup 7.1，中文向导、当前用户安装，包含运行环境检查和保留个人内容的升级/卸载。程序及安装器当前未签名，没有自动更新器、AI接入或云端同步，仍依赖.NET Framework 4.8或以上。

发布呈现参考：[Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher)、[CopyQ](https://github.com/hluk/CopyQ)、[Lively](https://github.com/lively-community/lively) 的下载入口、源码目录与 Release 附件组织。
