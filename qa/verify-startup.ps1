$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 "/out:$testPath\StartupVerification.exe" (Join-Path $projectPath 'StartupManager.cs') (Join-Path $PSScriptRoot 'StartupVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '登录启动验证编译失败' }
& (Join-Path $testPath 'StartupVerification.exe')
$testExitCode = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $testPath 'verification-startup.txt')
Write-Output "TestPath: $testPath"
if ($testExitCode -ne 0) { throw '登录启动验证失败' }
