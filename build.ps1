param([string]$OutputName = 'Yourself Copilot.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$brandIcon = Join-Path $PSScriptRoot 'icons\brand\companion.ico'
if (-not (Test-Path -LiteralPath $brandIcon)) { throw '缺少品牌 ICO，请先运行 qa\verify-brand.ps1 -RegenerateIcon' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$brandIcon" "/out:$PSScriptRoot\$OutputName" "/win32manifest:$PSScriptRoot\DesktopNote.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\WindowsFormsIntegration.dll" "$PSScriptRoot\DesktopNote.cs" "$PSScriptRoot\LibraryStore.cs" "$PSScriptRoot\ManagerWindow.cs" "$PSScriptRoot\NoteContext.cs" "$PSScriptRoot\LibraryVerification.cs" "$PSScriptRoot\WorkboardStore.cs" "$PSScriptRoot\WorkboardWindow.cs" "$PSScriptRoot\WorkboardVerification.cs" "$PSScriptRoot\WorkbenchWindow.cs" "$PSScriptRoot\StartupManager.cs" "$PSScriptRoot\AppTypography.cs" "$PSScriptRoot\AppVisuals.cs" "$PSScriptRoot\CornerResize.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output "编译完成：$OutputName"
