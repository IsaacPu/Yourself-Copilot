param([string]$SourcePath = '')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Join-Path $projectPath 'src' }
$SourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
$testPath = Join-Path $projectPath ('.qa\workbench-entry-failure-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
Copy-Item -LiteralPath (Join-Path $SourcePath 'WorkboardView.xaml') -Destination $testPath
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $SourcePath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\WorkbenchEntryFailureVerification.exe" /main:LocalDesktopNote.WorkbenchEntryFailureVerification "/win32manifest:$SourcePath\DesktopNote.manifest" @refs @sources (Join-Path $PSScriptRoot 'WorkbenchEntryFailureVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '工作台失败入口验证编译失败' }
$process = Start-Process -FilePath (Join-Path $testPath 'WorkbenchEntryFailureVerification.exe') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $testPath 'verification-workbench-entry-failure.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw '工作台失败入口验证失败，详情见报告' }
