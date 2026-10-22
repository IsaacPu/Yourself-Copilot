$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\shared-persistence-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
Copy-Item -LiteralPath (Join-Path $projectPath 'src\WorkboardView.xaml') -Destination $testPath
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\SharedPersistenceVerification.exe" /main:LocalDesktopNote.SharedPersistenceVerification "/win32manifest:$projectPath\src\DesktopNote.manifest" @refs @sources (Join-Path $PSScriptRoot 'SharedPersistenceVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '共享持久化验证编译失败' }
$process = Start-Process -FilePath (Join-Path $testPath 'SharedPersistenceVerification.exe') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $testPath 'verification-shared-persistence.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw '共享持久化验证失败，详情见报告' }
