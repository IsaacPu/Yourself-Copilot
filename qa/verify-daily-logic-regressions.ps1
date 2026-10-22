$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$reviewRoot = Join-Path $projectRoot ('.qa\review-next-regressions-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $reviewRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'WorkboardView.xaml') -Destination $reviewRoot
$reviewCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reviewWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$reviewRefs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$reviewWpf\WindowsBase.dll","/reference:$reviewWpf\PresentationCore.dll","/reference:$reviewWpf\PresentationFramework.dll","/reference:$reviewWpf\WindowsFormsIntegration.dll")
$reviewSources = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $reviewCompiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$reviewRoot\DailyLogicRegressionVerification.exe" /main:LocalDesktopNote.DailyLogicRegressionVerification "/win32manifest:$projectRoot\DesktopNote.manifest" @reviewRefs @reviewSources (Join-Path $PSScriptRoot 'DailyLogicRegressionVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '每日逻辑回归编译失败' }
$reviewProcess = Start-Process -FilePath (Join-Path $reviewRoot 'DailyLogicRegressionVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $reviewRoot 'verification-daily-logic-regressions.txt') -Encoding UTF8
Write-Output "ValidationRoot: $reviewRoot"
if ($reviewProcess.ExitCode -ne 0) { throw '每日逻辑回归存在失败项，详情见报告' }
