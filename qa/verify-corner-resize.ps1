param([Parameter(Mandatory=$true)][string]$SourceDirectory, [switch]$GeometryOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourceDirectory).Path
if ($sourceDirectoryPath.TrimEnd('\') -eq $projectRoot.TrimEnd('\')) { throw '请传入隔离 release 目录，禁止本专项编译尚未完成 UI 的项目根' }
if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectoryPath 'CornerResize.cs'))) { throw '指定 release 尚未提供 CornerResize.cs' }
$checkRoot = Join-Path $projectRoot ('.qa\corner-resize-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$snapshotRoot = Join-Path $checkRoot 'source'
New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
if ($GeometryOnly) { Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath 'CornerResize.cs') -Destination $snapshotRoot }
else {
    Get-ChildItem -LiteralPath $sourceDirectoryPath -Filter '*.cs' -File | Copy-Item -Destination $snapshotRoot
    foreach ($asset in @('WorkboardView.xaml','CompactWindow.xaml')) { Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath $asset) -Destination $checkRoot }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CornerResizeVerification.cs') -Destination $snapshotRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('/reference:System.Drawing.dll')
$options = @()
if ($GeometryOnly) { $options += '/define:GEOMETRY_ONLY' }
else { $references += @('/reference:System.Windows.Forms.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll"); $options += "/win32manifest:$sourceDirectoryPath\DesktopNote.manifest" }
$sources = @(Get-ChildItem -LiteralPath $snapshotRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$checkRoot\CornerResizeVerification.exe" /main:LocalDesktopNote.CornerResizeVerification @references @options @sources
if ($LASTEXITCODE -ne 0) { throw '四角缩放专项编译失败' }
$process = Start-Process -FilePath (Join-Path $checkRoot 'CornerResizeVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $checkRoot 'verification-corner-resize.txt') -Encoding UTF8
Write-Output "SourceDirectory: $sourceDirectoryPath"
Write-Output "ValidationRoot: $checkRoot"
if ($process.ExitCode -ne 0) { throw '四角缩放专项存在失败项' }
