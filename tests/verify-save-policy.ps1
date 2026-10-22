$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\save-policy-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
Copy-Item -LiteralPath (Join-Path $projectPath 'src\WorkboardView.xaml') -Destination $testPath
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\SavePolicyVerification.exe" /main:LocalDesktopNote.SavePolicyVerification "/win32manifest:$projectPath\src\DesktopNote.manifest" @refs @sources (Join-Path $PSScriptRoot 'SavePolicyVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '保存策略验证编译失败' }
$process = Start-Process -FilePath (Join-Path $testPath 'SavePolicyVerification.exe') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $testPath 'verification-save-policy.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw '保存策略验证失败，详情见报告' }
