$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\memo-paper-store-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /reference:System.Runtime.Serialization.dll /main:LocalDesktopNote.MemoPaperStoreVerification "/out:$testPath\MemoPaperStoreVerification.exe" (Join-Path $projectPath 'src\WorkboardStore.cs') (Join-Path $projectPath 'src\MemoPaper.cs') (Join-Path $PSScriptRoot 'DailyStoreVerification.cs') (Join-Path $PSScriptRoot 'MemoPaperStoreVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '随手记纸张存储验证编译失败' }
& (Join-Path $testPath 'MemoPaperStoreVerification.exe')
$testExitCode = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $testPath 'verification-memo-paper-store.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($testExitCode -ne 0) { throw '随手记纸张存储验证失败' }
