param([string]$SourcePath = '')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Join-Path $projectPath 'src' }
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourcePath).Path
$testPath = Join-Path $projectPath ('.qa\memo-paper-window-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$snapshotPath = Join-Path $testPath 'source'
New-Item -ItemType Directory -Path $snapshotPath -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectoryPath -Filter '*.cs' -File | Copy-Item -Destination $snapshotPath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MemoPaperWindowVerification.cs') -Destination $snapshotPath
foreach ($file in @('WorkboardView.xaml','CompactWindow.xaml')) {
    Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath $file) -Destination $testPath
}
foreach ($directory in @('fonts','icons','illustrations','sounds')) {
    Copy-Item -LiteralPath (Join-Path $projectPath ('assets\' + $directory)) -Destination $testPath -Recurse
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $snapshotPath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\MemoPaperWindowVerification.exe" /main:LocalDesktopNote.MemoPaperWindowVerification "/win32manifest:$sourceDirectoryPath\DesktopNote.manifest" @refs @sources
if ($LASTEXITCODE -ne 0) { throw '随手记纸页真实窗口验证编译失败' }
$process = Start-Process -FilePath (Join-Path $testPath 'MemoPaperWindowVerification.exe') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $testPath 'verification-memo-paper-window.txt') -Encoding UTF8
Write-Output "TestPath: $testPath"
if ($process.ExitCode -ne 0) { throw '随手记纸页真实窗口验证失败，详情见报告' }
