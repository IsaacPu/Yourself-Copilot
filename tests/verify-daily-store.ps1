$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\daily-store-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /reference:System.Runtime.Serialization.dll "/out:$testPath\DailyStoreVerification.exe" (Join-Path $projectPath 'src\WorkboardStore.cs') (Join-Path $PSScriptRoot 'DailyStoreVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '日期与历史存储验证编译失败' }
& (Join-Path $testPath 'DailyStoreVerification.exe')
$testExitCode = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $testPath 'verification-daily-store.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($testExitCode -ne 0) { throw '日期与历史存储验证失败' }
