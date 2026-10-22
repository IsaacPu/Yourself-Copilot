param([string]$ApplicationName = 'Yourself Copilot.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$checkRoot = Join-Path $taskRoot ('.qa\daily-integration-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $checkRoot | Out-Null
$runtimeRoot = Join-Path $taskRoot 'dist\Yourself-Copilot'
foreach ($name in @('WorkboardView.xaml','CompactWindow.xaml',$ApplicationName,'fonts','icons')) {
    Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $checkRoot -Recurse
}
& (Join-Path $PSScriptRoot 'New-TestImage.ps1') -OutputPath (Join-Path $checkRoot '计划表.png')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$checkRoot\DailyIntegrationVerification.exe" /main:LocalDesktopNote.DailyIntegrationVerification "/win32manifest:$taskRoot\src\DesktopNote.manifest" @references @sources (Join-Path $PSScriptRoot 'DailyIntegrationVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '每日任务集成检查编译失败' }
$run = Start-Process -FilePath (Join-Path $checkRoot 'DailyIntegrationVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $checkRoot 'verification-daily-integration.txt')
Write-Output "ValidationRoot: $checkRoot"
if ($run.ExitCode -ne 0) { throw '每日任务集成检查失败' }
