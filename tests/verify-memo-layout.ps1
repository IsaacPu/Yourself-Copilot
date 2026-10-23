param([string]$SourcePath = '')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Join-Path $projectPath 'src' }
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourcePath).Path
$testPath = Join-Path $projectPath ('.qa\memo-layout-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$snapshotPath = Join-Path $testPath 'source'
New-Item -ItemType Directory -Path $snapshotPath -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectoryPath -Filter '*.cs' -File | Copy-Item -Destination $snapshotPath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MemoLayoutVerification.cs') -Destination $snapshotPath
Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath 'WorkboardView.xaml') -Destination $testPath
foreach ($directory in @('fonts','icons','illustrations')) {
    Copy-Item -LiteralPath (Join-Path $projectPath ('assets\' + $directory)) -Destination $testPath -Recurse
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $snapshotPath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\MemoLayoutVerification.exe" /main:LocalDesktopNote.MemoLayoutVerification "/win32manifest:$sourceDirectoryPath\DesktopNote.manifest" @refs @sources
if ($LASTEXITCODE -ne 0) { throw '随手记布局验证编译失败' }
$process = Start-Process -FilePath (Join-Path $testPath 'MemoLayoutVerification.exe') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $testPath 'verification-memo-layout.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw '随手记布局验证失败，详情见报告' }
