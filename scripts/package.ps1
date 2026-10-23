param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()
}

# The build validates the version before changing its output.
& (Join-Path $PSScriptRoot 'build.ps1') -Version $Version
$runtimeRoot = Join-Path $projectRoot 'dist\Yourself-Copilot'
$executable = Join-Path $runtimeRoot 'Yourself Copilot.exe'
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if ($fileVersion.FileVersion -ne ($Version + '.0') -or $fileVersion.ProductVersion -ne $Version) {
    throw 'The executable version does not match the package version.'
}

$stageRoot = Join-Path $projectRoot ('.qa\package-' + [Guid]::NewGuid().ToString('N'))
$payloadRoot = Join-Path $stageRoot 'Yourself-Copilot'
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

# Do not copy a whole runtime folder: it may contain someone's local data.
foreach ($name in @('Yourself Copilot.exe','WorkboardView.xaml','CompactWindow.xaml')) {
    Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $payloadRoot
}
foreach ($name in @('fonts','icons','illustrations')) {
    $assetRoot = Join-Path $projectRoot ('assets\' + $name)
    $links = @(Get-ChildItem -LiteralPath $assetRoot -Recurse -Force | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    })
    if ($links.Count) { throw 'Release assets must not contain filesystem links.' }
    Copy-Item -LiteralPath $assetRoot -Destination $payloadRoot -Recurse
}
$utf8Bom = New-Object Text.UTF8Encoding($true)
foreach ($document in @(@('PORTABLE.txt','README.txt'),@('THIRD-PARTY-NOTICES.txt','THIRD-PARTY-NOTICES.txt'))) {
    $content = [IO.File]::ReadAllText((Join-Path $projectRoot ('docs\' + $document[0])), [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $payloadRoot $document[1]), $content, $utf8Bom)
}
[IO.File]::WriteAllText((Join-Path $payloadRoot 'VERSION.txt'), $Version + "`r`n", [Text.Encoding]::ASCII)

$payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse)
$forbiddenFiles = @($payloadFiles | Where-Object {
    $_.Name -match '^(workboard|library|settings).*\.json|recovery|运行日志|^计划表\.png$' -or
    $_.Extension -in @('.cs','.ps1','.bak','.tmp','.log')
})
if ($forbiddenFiles.Count) { throw 'Unexpected source or local data in the release payload.' }
foreach ($path in @('images','history','.qa')) {
    if (Test-Path -LiteralPath (Join-Path $payloadRoot $path)) { throw 'Local data directory in release payload.' }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipName = 'Yourself-Copilot-windows-x64.zip'
$temporaryZip = Join-Path $stageRoot $zipName
[IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $true)
$outputRoot = Join-Path $projectRoot 'dist'
$outputZip = Join-Path $outputRoot $zipName
Copy-Item -LiteralPath $temporaryZip -Destination $outputZip -Force
$digest = (Get-FileHash -LiteralPath $outputZip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $outputRoot 'SHA256SUMS.txt'
[IO.File]::WriteAllText($checksumPath, $digest + '  ' + $zipName + "`n", [Text.Encoding]::ASCII)
Write-Output ('Version: ' + $Version)
Write-Output ('Payload files: ' + $payloadFiles.Count)
Write-Output ('ZIP: ' + $outputZip)
Write-Output ('SHA256: ' + $digest)
Write-Output ('Checksums: ' + $checksumPath)
