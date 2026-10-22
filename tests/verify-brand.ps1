param([switch]$RegenerateIcon)
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$checkPath = Join-Path $projectPath ('.qa\brand-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$sourcePath = Join-Path $checkPath 'source'
New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' -File | Copy-Item -Destination $sourcePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BrandVerification.cs') -Destination $sourcePath
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $sourcePath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$runnerPath = Join-Path $checkPath 'BrandVerification.exe'
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$runnerPath" /main:LocalDesktopNote.BrandVerification @refs @sources
if ($LASTEXITCODE -ne 0) { throw '品牌验证编译失败' }
$generationPath = Join-Path $checkPath 'generation'
New-Item -ItemType Directory -Path (Join-Path $generationPath 'icons\brand') -Force | Out-Null
Copy-Item -LiteralPath $runnerPath -Destination $generationPath
Copy-Item -LiteralPath (Join-Path $projectPath 'assets\icons\brand\companion.svg') -Destination (Join-Path $generationPath 'icons\brand')
if ($RegenerateIcon) {
    $process = Start-Process -FilePath (Join-Path $generationPath 'BrandVerification.exe') -ArgumentList 'generate' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'ICO 生成失败' }
    Copy-Item -LiteralPath (Join-Path $generationPath 'icons\brand\companion.ico') -Destination (Join-Path $projectPath 'assets\icons\brand\companion.ico')
}
foreach ($mode in @('bundled','missing','corrupt')) {
    $modePath = Join-Path $checkPath $mode
    New-Item -ItemType Directory -Path $modePath -Force | Out-Null
    Copy-Item -LiteralPath $runnerPath -Destination $modePath
    Copy-Item -LiteralPath (Join-Path $projectPath 'src\WorkboardView.xaml') -Destination $modePath
    Copy-Item -LiteralPath (Join-Path $projectPath 'assets\icons') -Destination $modePath -Recurse
    if ($mode -ne 'bundled') {
        # Only this isolated fixture is changed; production assets remain untouched.
        foreach ($name in @('companion.svg','companion.ico')) {
            $fixturePath = Join-Path $modePath ('icons\brand\' + $name)
            if ($mode -eq 'missing') { Remove-Item -LiteralPath $fixturePath }
            else { [IO.File]::WriteAllText($fixturePath, 'invalid asset') }
        }
    }
    $process = Start-Process -FilePath (Join-Path $modePath 'BrandVerification.exe') -ArgumentList $mode -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $modePath 'verification-brand.txt') -Encoding UTF8
    if ($process.ExitCode -ne 0) { throw "品牌验证失败：$mode" }
}
$candidatePath = Join-Path $checkPath 'bundled\Yourself Copilot-brand-candidate.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$candidatePath" /main:LocalDesktopNote.Program "/win32manifest:$projectPath\src\DesktopNote.manifest" "/win32icon:$projectPath\assets\icons\brand\companion.ico" @refs @sources
if ($LASTEXITCODE -ne 0) { throw '品牌候选编译失败' }
$process = Start-Process -FilePath (Join-Path $checkPath 'bundled\BrandVerification.exe') -ArgumentList @('resource',('"' + $candidatePath + '"')) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $checkPath 'bundled\verification-brand-resource.txt') -Encoding UTF8
if ($process.ExitCode -ne 0) { throw '品牌候选 EXE 原生图标资源验证失败' }
foreach ($check in @(@('src\NoteContext.cs','Icon = AppVisuals.ApplicationIcon'),@('src\WorkbenchWindow.cs','AppVisuals.BrandIcon(27)'),@('src\WorkboardWindow.cs','AppVisuals.BrandIcon(23)'),@('scripts\build.ps1','/win32icon:$brandIcon'))) {
    if (-not ([IO.File]::ReadAllText((Join-Path $projectPath $check[0])).Contains($check[1]))) { throw "品牌引用缺失：$($check[0])" }
}
Write-Output 'PASS: tray, workbench, desktop card and compiler reference the independent brand assets'
Write-Output "CandidatePath: $candidatePath"
Write-Output "ValidationRoot: $checkPath"
