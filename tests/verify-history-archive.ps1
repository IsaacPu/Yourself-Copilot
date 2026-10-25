param([string]$SourcePath = '', [switch]$Preview)
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Join-Path $projectPath 'src' }
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourcePath).Path
$testPath = Join-Path $projectPath ('.qa\hv-' + (Get-Date -Format 'MMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 4))
$snapshotPath = Join-Path $testPath 'src'
New-Item -ItemType Directory -Path $snapshotPath -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectoryPath -Filter '*.cs' -File | Copy-Item -Destination $snapshotPath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HistoryArchiveVerification.cs') -Destination $snapshotPath
foreach ($name in @('WorkboardView.xaml','CompactWindow.xaml')) {
    $sourceFile = Join-Path $sourceDirectoryPath $name
    if (Test-Path -LiteralPath $sourceFile) { Copy-Item -LiteralPath $sourceFile -Destination $testPath }
}
foreach ($directory in @('fonts','icons','illustrations','sounds')) {
    Copy-Item -LiteralPath (Join-Path $projectPath ('assets\' + $directory)) -Destination $testPath -Recurse
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $snapshotPath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$testPath\HistoryArchiveVerification.exe" /main:LocalDesktopNote.HistoryArchiveVerification "/win32manifest:$sourceDirectoryPath\DesktopNote.manifest" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'History archive verification compilation failed.' }
if ($Preview) {
    $process = Start-Process -FilePath (Join-Path $testPath 'HistoryArchiveVerification.exe') -ArgumentList '--preview' -WindowStyle Normal -PassThru
    Write-Output "PreviewProcessId: $($process.Id)"
    Write-Output "PreviewExe: $testPath\HistoryArchiveVerification.exe"
} else {
    $process = Start-Process -FilePath (Join-Path $testPath 'HistoryArchiveVerification.exe') -ArgumentList '--verify' -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $testPath 'verification-history-archive.txt') -Encoding UTF8
    Write-Output "TestPath: $testPath"
    if ($process.ExitCode -ne 0) { throw 'History archive verification failed; see its report.' }
}
