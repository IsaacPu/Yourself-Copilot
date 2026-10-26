# Windows安装器

安装器封装已核验的程序ZIP，不会重新构建或混入工作区的新功能。当前应用仍为v0.2.0，公开附件为 `Yourself-Copilot-Setup-0.2.0-x64.exe`。

## 流程与边界

中文欢迎页 → 全新安装/升级免安装版 → 安装位置 → 可选桌面快捷方式 → 确认 → 安装进度 → 完成并打开应用。升级免安装版时增加原文件夹选择页；已有安装版跳过方式选择并沿用原位置。

界面使用现有品牌标志、暖白底和纸页图案，文字与按钮使用原生向导控件。无额外软件、强制开机启动或虚构的许可同意。默认当前用户安装，无需管理员权限。

安装器检查Windows 10/11 x64兼容环境及.NET Framework 4.8或以上；缺少.NET时给出微软地址并停止，不自动下载或改变系统环境。安装前检查目录可写、程序已退出及文件版本，避免用旧程序覆盖更新版本。

本版本沿用应用的同目录数据格式，默认路径为 `%LOCALAPPDATA%\Programs\Yourself Copilot`。旧ZIP版选择原文件夹就地升级，不迁移内容，不修改图片的已有绝对路径。建议升级前退出并备份整个文件夹。

只有41个程序资源白名单文件进入安装包。没有递归删除目录的卸载命令；个人JSON、备份、图片、历史及恢复文件不属于安装文件。卸载后它们仍留在原目录。卸载只清理精确指向本安装位置的登录启动项，指向其他副本的同名项保留。

## 构建

使用官方Inno Setup 7.1.0 x64编译器，可按官方支持的 `/PORTABLE=1` 将编译工具放在本地隔离目录。官方编译器安装文件SHA256为 `0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f`；来源：<https://github.com/jrsoftware/issrc/releases/tag/is-7_1_0>。遵守其使用许可。

先构建并验收载荷ZIP，取得实际SHA256，再执行：

```powershell
.\scripts\build-installer.ps1 `
  -ZipPath .\dist\Yourself-Copilot-windows-x64.zip `
  -ExpectedSha256 '<已核验的ZIP SHA256>' `
  -CompilerPath 'C:\Tools\Inno Setup\ISCC.exe' `
  -Version 0.2.0
```

脚本核对ZIP哈希、文件白名单与EXE/VERSION版本，输出安装EXE及本地JSON构建清单。清单记录载荷、安装包及编译器哈希，不作为用户下载附件。`README.txt`转换为安装版说明，其余程序文件不变。

## 隔离验证

加 `-TestIdentity -OutputDirectory .qa\installer-test-output` 生成测试安装包，使用独立AppId、快捷方式、互斥体及启动项名称。不要向用户分发测试包。

```powershell
.\tests\verify-installer.ps1 `
  -ManifestPath .qa\installer-test-output\Yourself-Copilot-Setup-0.2.0-x64.exe.json
```

测试实际运行安装、升级和卸载，在`.qa/`的中文、空格及`&`路径中核对全部41个载荷文件、Windows注册与快捷方式、两个隔离应用模式、运行中保护、拒绝降级、8份模拟个人数据的保留，以及卸载后重新安装。启动项检查只操作独立测试名称。卸载器原进程可能提前退出，检查等待临时工作进程关闭日志后才读取结果。

本次129项检查通过，原ZIP的223项载荷验证仍适用。安装页面的真实排版及辅助技术尚未验收：Computer Use运行环境初始化返回“failed to write kernel assets: 系统找不到指定的路径”，重置后重试仍失败。无数字签名证书，安装包不宣称已签名或获得SmartScreen信誉。
