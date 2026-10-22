param([string]$SourceDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourceDirectory).Path
$checkRoot = Join-Path $projectRoot ('.qa\edge-resize-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$snapshotRoot = Join-Path $checkRoot 'source'
New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectoryPath -Filter '*.cs' -File | Copy-Item -Destination $snapshotRoot
foreach ($asset in @('WorkboardView.xaml','CompactWindow.xaml')) { Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath $asset) -Destination $checkRoot }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EdgeResizeVerification.cs') -Destination $snapshotRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $snapshotRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$checkRoot\EdgeResizeVerification.exe" /main:LocalDesktopNote.EdgeResizeVerification "/win32manifest:$sourceDirectoryPath\DesktopNote.manifest" @references @sources
if ($LASTEXITCODE -ne 0) { throw '八方向缩放专项编译失败' }
$process = Start-Process -FilePath (Join-Path $checkRoot 'EdgeResizeVerification.exe') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $checkRoot 'verification-edge-resize.txt') -Encoding UTF8
Write-Output "ValidationRoot: $checkRoot"
if ($process.ExitCode -ne 0) { throw '八方向缩放专项存在失败项' }
