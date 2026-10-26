param(
    [Parameter(Mandatory=$true)][string]$ZipPath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][string]$CompilerPath,
    [string]$Version = '0.2.0',
    [string]$OutputDirectory = '',
    [switch]$TestIdentity
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$' -or @($Version.Split('.') | Where-Object { [int]$_ -gt 65534 }).Count) { throw 'Invalid version.' }
$ZipPath = (Resolve-Path -LiteralPath $ZipPath).ProviderPath
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).ProviderPath
if ((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Payload ZIP SHA-256 mismatch.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot 'dist' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $projectRoot ('.qa\installer-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null

# Extract only reviewed paths. Never package a live runtime or arbitrary ZIP entries.
$allowed = @([IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'installer\payload-files.txt')) | Where-Object { $_.Trim() })
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $seen = @{}
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if (-not $name.StartsWith('Yourself-Copilot/',[StringComparison]::Ordinal)) { throw 'Unexpected archive root.' }
        $relative = $name.Substring('Yourself-Copilot/'.Length)
        if ($allowed -cnotcontains $relative -or $seen.ContainsKey($relative)) { throw ('Unexpected/duplicate payload file: ' + $relative) }
        $seen[$relative] = $true
        $destination = Join-Path $stage ('payload\' + $relative)
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false)
    }
    if ($seen.Count -ne $allowed.Count) { throw 'Payload file missing.' }
}
finally { $archive.Dispose() }
$payload = Join-Path $stage 'payload'
$executable = Join-Path $payload 'Yourself Copilot.exe'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion -ne ($Version + '.0') -or
    [IO.File]::ReadAllText((Join-Path $payload 'VERSION.txt')).Trim() -ne $Version) { throw 'Payload version mismatch.' }

$installerReadme = @"
Yourself Copilot $Version

开始：从开始菜单或桌面快捷方式打开 Yourself Copilot。
关闭工作台后会收起到托盘；完整退出请使用托盘菜单。
登录自动运行可在应用设置中开启。

内容保存在此文件夹，没有云端同步，本地数据未加密。
备份前先从托盘退出，然后复制整个文件夹。
更新时运行新安装包，沿用原位置，个人内容会保留。
卸载请前往 Windows“已安装的应用”；个人内容保留，可重新安装到这里继续使用。

使用指南：https://github.com/IsaacPu/Yourself-Copilot/blob/main/docs/USAGE.md
反馈：https://github.com/IsaacPu/Yourself-Copilot/issues
"@
[IO.File]::WriteAllText((Join-Path $payload 'README.txt'),$installerReadme,[Text.UTF8Encoding]::new($true))
$lines = foreach ($relative in $allowed) {
    $destination = [IO.Path]::GetDirectoryName($relative.Replace('/','\'))
    $flags = 'ignoreversion'
    if ($relative -eq 'Yourself Copilot.exe' -or $relative -eq 'VERSION.txt') { $flags += ' dontcopy' }
    # dontcopy files are separately installed below, as well as extracted for preflight.
    'Source: "' + (Join-Path $payload $relative) + '"; DestDir: "{app}' + $(if ($destination) { '\' + $destination }) + '"; Flags: ' + $flags
    if ($flags.Contains('dontcopy')) {
        'Source: "' + (Join-Path $payload $relative) + '"; DestDir: "{app}"; Flags: ignoreversion'
    }
}
$include = Join-Path $stage 'files.iss'
[IO.File]::WriteAllLines($include,$lines,[Text.UTF8Encoding]::new($true))

# Brand artwork is derived from the existing project icon, without new image assets.
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::new((Join-Path $payload 'icons\brand\companion.ico'),256,256)
$brand = $icon.ToBitmap()
foreach ($size in @(@(328,628,'welcome.bmp'),@(110,110,'header.bmp'))) {
    $bitmap = [Drawing.Bitmap]::new($size[0],$size[1])
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#FAF7F3'))
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        if ($size[1] -gt 200) {
            $paper = [Drawing.SolidBrush]::new([Drawing.Color]::White)
            $line = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#DDDCD3'),2)
            try {
                $graphics.FillRectangle($paper,42,252,244,268)
                $graphics.DrawRectangle($line,42,252,244,268)
                foreach ($y in @(332,370,408,446)) { $graphics.DrawLine($line,72,$y,252,$y) }
                $graphics.DrawImage($brand,100,96,128,128)
            }
            finally { $paper.Dispose(); $line.Dispose() }
        } else { $graphics.DrawImage($brand,19,19,72,72) }
        $bitmap.Save((Join-Path $stage $size[2]),[Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$brand.Dispose(); $icon.Dispose()
$definitions = @(('/DAppVersion=' + $Version), ('/DBrandIcon=' + (Join-Path $payload 'icons\brand\companion.ico')),
    ('/DWizardImage=' + (Join-Path $stage 'welcome.bmp')), ('/DWizardSmallImage=' + (Join-Path $stage 'header.bmp')),
    ('/DOutputPath=' + $OutputDirectory), ('/DFilesInclude=' + $include))
$testId = ''
if ($TestIdentity) {
    $testId = 'YourselfCopilot.QA.' + [Guid]::NewGuid().ToString('N')
    $definitions += @(('/DInstallerId=' + $testId), ('/DShortcutName=' + $testId), ('/DApplicationMutex=Local\' + $testId), ('/DStartupValueName=' + $testId))
}
& $CompilerPath /Q @definitions (Join-Path $PSScriptRoot 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$setup = Join-Path $OutputDirectory ('Yourself-Copilot-Setup-' + $Version + '-x64.exe')
$manifest = [ordered]@{
    Version=$Version; PayloadSha256=$ExpectedSha256.ToLowerInvariant();
    Setup=$setup; SetupSha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant();
    CompilerSha256=(Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash.ToLowerInvariant();
    TestIdentity=$testId; Stage=$stage; Files=@($allowed)
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($setup + '.json') -Encoding UTF8
Write-Output ('Installer: ' + $setup)
Write-Output ('SHA256: ' + $manifest.SetupSha256)
