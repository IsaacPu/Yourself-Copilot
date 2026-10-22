param(
    [string]$OutputName = 'Yourself Copilot.exe',
    [string]$Version = '0.1.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$versionFile = Join-Path $projectRoot 'VERSION'
if (-not $PSBoundParameters.ContainsKey('Version') -and (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    $Version = [IO.File]::ReadAllText($versionFile).Trim()
}
if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$' -or @($Version.Split('.') | Where-Object { [int]$_ -gt 65534 }).Count -gt 0) {
    throw 'Version 必须为三段非负整数，每段不超过 65534，例如 0.1.0。'
}
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or $OutputName -notmatch '^[^<>:"/\\|?*]+\.exe$') {
    throw 'OutputName 必须是不含路径的 .exe 文件名。'
}
$sourceRoot = Join-Path $projectRoot 'src'
$assetRoot = Join-Path $projectRoot 'assets'
$outputRoot = Join-Path $projectRoot 'dist\Yourself-Copilot'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$brandIcon = Join-Path $assetRoot 'icons\brand\companion.ico'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw '缺少 Windows x64 .NET Framework 4.x 编译器。' }
if (-not (Test-Path -LiteralPath $brandIcon -PathType Leaf)) { throw '缺少品牌 ICO，请先运行 tests\verify-brand.ps1 -RegenerateIcon。' }
$buildDirectory = Join-Path $projectRoot ('.qa\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $buildDirectory,$outputRoot -Force | Out-Null
$assemblyInfo = Join-Path $buildDirectory 'AssemblyInfo.g.cs'
$assemblyVersion = $Version + '.0'
$metadata = @"
using System.Reflection;
[assembly: AssemblyTitle("Yourself Copilot")]
[assembly: AssemblyProduct("Yourself Copilot")]
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: AssemblyInformationalVersion("$Version")]
"@
[IO.File]::WriteAllText($assemblyInfo, $metadata, [Text.UTF8Encoding]::new($true))
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
$references = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$executable = Join-Path $outputRoot $OutputName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$brandIcon" "/out:$executable" "/win32manifest:$sourceRoot\DesktopNote.manifest" @references @sources $assemblyInfo
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
foreach ($name in @('WorkboardView.xaml','CompactWindow.xaml')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $outputRoot -Force
}
foreach ($directory in @('fonts','icons')) {
    Copy-Item -LiteralPath (Join-Path $assetRoot $directory) -Destination $outputRoot -Recurse -Force
}
Write-Output "Build complete: $executable (v$Version)"
