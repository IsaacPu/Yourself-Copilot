$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$reviewRoot = Join-Path $projectRoot ('.qa\review-next-library-recovery-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $reviewRoot | Out-Null
$sourceRoot = Join-Path $reviewRoot 'source'
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | Copy-Item -Destination $sourceRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LibraryRecoveryVerification.cs') -Destination $sourceRoot
$reviewCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reviewWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$reviewRefs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$reviewWpf\WindowsBase.dll","/reference:$reviewWpf\PresentationCore.dll","/reference:$reviewWpf\PresentationFramework.dll","/reference:$reviewWpf\WindowsFormsIntegration.dll")
$reviewSources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $reviewCompiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$reviewRoot\LibraryRecoveryVerification.exe" /main:LocalDesktopNote.LibraryRecoveryVerification "/win32manifest:$projectRoot\src\DesktopNote.manifest" @reviewRefs @reviewSources
if ($LASTEXITCODE -ne 0) { throw '图片库恢复回归编译失败' }
$reviewProcess = Start-Process -FilePath (Join-Path $reviewRoot 'LibraryRecoveryVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $reviewRoot 'verification-library-recovery.txt') -Encoding UTF8
Write-Output "ValidationRoot: $reviewRoot"
if ($reviewProcess.ExitCode -ne 0) { throw '图片库恢复回归存在失败项，详情见报告' }
