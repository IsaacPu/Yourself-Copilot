param([string]$ApplicationName = 'Yourself Copilot.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$validationRoot = Join-Path $projectRoot ('.qa\validation-v3-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $validationRoot | Out-Null
$runtimeRoot = Join-Path $projectRoot 'dist\Yourself-Copilot'
foreach ($name in @($ApplicationName,'CompactWindow.xaml','WorkboardView.xaml','fonts','icons')) {
    Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $validationRoot -Recurse
}
& (Join-Path $PSScriptRoot 'New-TestImage.ps1') -OutputPath (Join-Path $validationRoot '计划表.png')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
foreach ($entry in @('NativeFlowVerification','WorkboardPreview','TaskFlowVerification')) {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$validationRoot\$entry.exe" "/main:LocalDesktopNote.$entry" "/win32manifest:$projectRoot\src\DesktopNote.manifest" @references @sources (Join-Path $PSScriptRoot ($entry + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw "验证程序编译失败：$entry" }
}
foreach ($argument in @('--self-test','--verify-library','--smoke-test','--verify-workboard')) {
    $checkProcess = Start-Process -FilePath (Join-Path $validationRoot $ApplicationName) -ArgumentList $argument -WindowStyle Hidden -Wait -PassThru
    if ($checkProcess.ExitCode -ne 0) { throw "验证失败：$argument。记录保存在 $validationRoot" }
}
$flowProcess = Start-Process -FilePath (Join-Path $validationRoot 'NativeFlowVerification.exe') -WindowStyle Hidden -Wait -PassThru
if ($flowProcess.ExitCode -ne 0) { throw "图片流程验证失败。记录保存在 $validationRoot" }
$taskFlowProcess = Start-Process -FilePath (Join-Path $validationRoot 'TaskFlowVerification.exe') -WindowStyle Hidden -Wait -PassThru
if ($taskFlowProcess.ExitCode -ne 0) { throw "任务流程验证失败。记录保存在 $validationRoot" }
Get-ChildItem -LiteralPath $validationRoot -Filter 'verification*.txt' | ForEach-Object {
    [pscustomobject]@{ Report = $_.Name; Passed = @(Select-String -LiteralPath $_.FullName -Pattern '^PASS:').Count }
}
Write-Output "ValidationRoot: $validationRoot"
