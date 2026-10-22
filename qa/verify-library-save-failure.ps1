param([string]$NoteContextSnapshot)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($NoteContextSnapshot -and -not (Test-Path -LiteralPath $NoteContextSnapshot -PathType Leaf)) { throw '指定的 NoteContext 源码快照不存在' }
$reviewRoot = Join-Path $projectRoot ('.qa\review-next-library-save-failure-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $reviewRoot | Out-Null
$sourceRoot = Join-Path $reviewRoot 'source'
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File | Where-Object { -not $NoteContextSnapshot -or $_.Name -ne 'NoteContext.cs' } | Copy-Item -Destination $sourceRoot
if ($NoteContextSnapshot) { Copy-Item -LiteralPath $NoteContextSnapshot -Destination (Join-Path $sourceRoot 'NoteContext.cs') }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LibrarySaveFailureVerification.cs') -Destination $sourceRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'CompactWindow.xaml') -Destination $reviewRoot
$reviewCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reviewWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$reviewRefs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$reviewWpf\WindowsBase.dll","/reference:$reviewWpf\PresentationCore.dll","/reference:$reviewWpf\PresentationFramework.dll","/reference:$reviewWpf\WindowsFormsIntegration.dll")
$reviewSources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $reviewCompiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$reviewRoot\LibrarySaveFailureVerification.exe" /main:LocalDesktopNote.LibrarySaveFailureVerification "/win32manifest:$projectRoot\DesktopNote.manifest" @reviewRefs @reviewSources
if ($LASTEXITCODE -ne 0) { throw '图片库保存失败检查编译失败' }
$reviewProcess = Start-Process -FilePath (Join-Path $reviewRoot 'LibrarySaveFailureVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $reviewRoot 'verification-library-save-failure.txt') -Encoding UTF8
Write-Output "SourceMode: $(if ($NoteContextSnapshot) { 'explicit NoteContext snapshot' } else { 'current source' })"
Write-Output "ValidationRoot: $reviewRoot"
if ($reviewProcess.ExitCode -ne 0) { throw "图片库保存失败检查未通过，ExitCode=$($reviewProcess.ExitCode)" }
