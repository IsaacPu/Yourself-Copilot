$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $projectPath ('.qa\typography-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6) + '\中文 QA & space')
New-Item -ItemType Directory -Path $testPath | Out-Null

function Get-FontRegistrationSnapshot {
    $entries = foreach ($path in @('Software\Microsoft\Windows NT\CurrentVersion\Fonts')) {
        foreach ($hive in @([Microsoft.Win32.Registry]::CurrentUser, [Microsoft.Win32.Registry]::LocalMachine)) {
            $key = $hive.OpenSubKey($path, $false)
            try {
                if ($null -ne $key) {
                    foreach ($name in @($key.GetValueNames() | Sort-Object)) {
                        $key.Name + '|' + $name + '|' + $key.GetValueKind($name) + '|' + [string]$key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                    }
                }
            }
            finally { if ($null -ne $key) { $key.Dispose() } }
        }
    }
    return ($entries -join "`n")
}

$registrationsBefore = Get-FontRegistrationSnapshot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll',"/reference:$wpf\WindowsBase.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsFormsIntegration.dll")
$sources = @(Get-ChildItem -LiteralPath $projectPath -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$runnerPath = Join-Path $testPath 'TypographyVerification.exe'
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$runnerPath" /main:LocalDesktopNote.TypographyVerification "/win32manifest:$projectPath\DesktopNote.manifest" @refs @sources (Join-Path $PSScriptRoot 'TypographyVerification.cs')
if ($LASTEXITCODE -ne 0) { throw '字体验证编译失败' }

foreach ($mode in @('bundled','missing','corrupt','memo-missing')) {
    $modePath = Join-Path $testPath $mode
    New-Item -ItemType Directory -Path $modePath | Out-Null
    Copy-Item -LiteralPath $runnerPath -Destination $modePath
    Copy-Item -LiteralPath (Join-Path $projectPath 'WorkboardView.xaml') -Destination $modePath
    if ($mode -eq 'bundled') { Copy-Item -LiteralPath (Join-Path $projectPath 'fonts') -Destination $modePath -Recurse }
    if ($mode -eq 'memo-missing') {
        $fontPath = Join-Path $modePath 'fonts'
        New-Item -ItemType Directory -Path $fontPath | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectPath 'fonts\NotoSansSC') -Destination $fontPath -Recurse
    }
    if ($mode -eq 'corrupt') {
        foreach ($font in @('NotoSansSC\NotoSansSC-Regular.otf','LXGWWenKai\LXGWWenKai-Regular.ttf')) {
            $corruptPath = Join-Path $modePath ('fonts\' + $font)
            New-Item -ItemType Directory -Path (Split-Path $corruptPath -Parent) -Force | Out-Null
            [IO.File]::WriteAllBytes($corruptPath, [byte[]]@(0,1,2,3))
        }
    }
    $process = Start-Process -FilePath (Join-Path $modePath 'TypographyVerification.exe') -ArgumentList $mode -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $modePath 'verification-typography.txt') -Encoding UTF8
    if ($process.ExitCode -ne 0) { throw "字体验证失败：$mode，记录位于 $modePath" }
}
if ((Get-FontRegistrationSnapshot) -cne $registrationsBefore) { throw '字体注册表状态发生变化，请检查并发的系统字体操作。验证未执行任何字体安装。' }
Write-Output 'PASS: current-user and machine font registration entries are unchanged'
Write-Output "TestPath: $testPath"
