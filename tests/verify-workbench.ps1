$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\workbench-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 "/out:$testPath\WorkbenchControlVerification.exe" /main:WorkbenchControlVerification "/win32manifest:$projectPath\src\DesktopNote.manifest" @refs @sources (Join-Path $PSScriptRoot 'WorkbenchControlVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '工作台验证编译失败' }
$outputPath = Join-Path $testPath 'verification-workbench.txt'
$errorPath = Join-Path $testPath 'verification-workbench-errors.txt'
$process = Start-Process -FilePath (Join-Path $testPath 'WorkbenchControlVerification.exe') -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $outputPath -RedirectStandardError $errorPath
Get-Content -LiteralPath $outputPath -Encoding UTF8
if ((Get-Item -LiteralPath $errorPath).Length -gt 0) { Get-Content -LiteralPath $errorPath -Encoding UTF8 }
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw '工作台验证失败' }
