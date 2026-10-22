$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $taskRoot ('.qa\inline-transition-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'WorkboardView.xaml') -Destination $testRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs=@('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources=@(Get-ChildItem -LiteralPath $taskRoot -Filter '*.cs' -File | ForEach-Object {$_.FullName})
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$testRoot\EditTransitionVerification.exe" /main:LocalDesktopNote.EditTransitionVerification "/win32manifest:$taskRoot\DesktopNote.manifest" @refs @sources (Join-Path $PSScriptRoot 'EditTransitionVerification.cs')
if($LASTEXITCODE -ne 0){throw '输入过渡检查编译失败'}
$run=Start-Process -FilePath (Join-Path $testRoot 'EditTransitionVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $testRoot 'verification-inline-transition.txt')
Write-Output "ValidationRoot: $testRoot"
if($run.ExitCode -ne 0){throw '输入过渡检查失败'}
