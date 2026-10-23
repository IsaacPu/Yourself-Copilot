param([string]$SourcePath = '')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Join-Path $projectPath 'src' }
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourcePath).Path
$testPath = Join-Path $projectPath ('.qa\memo-history-window-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$snapshotPath = Join-Path $testPath 'source'
New-Item -ItemType Directory -Path $snapshotPath -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectoryPath -Filter '*.cs' -File | Copy-Item -Destination $snapshotPath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MemoHistoryWindowVerification.cs') -Destination $snapshotPath
Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath 'WorkboardView.xaml') -Destination $testPath
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $snapshotPath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\MemoHistoryWindowVerification.exe" /main:LocalDesktopNote.MemoHistoryWindowVerification "/win32manifest:$sourceDirectoryPath\DesktopNote.manifest" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Memo history window verification compilation failed' }
$process = Start-Process -FilePath (Join-Path $testPath 'MemoHistoryWindowVerification.exe') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $testPath 'verification-memo-history-window.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw 'Memo history window verification failed; see the report' }
