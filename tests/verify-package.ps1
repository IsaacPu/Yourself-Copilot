param(
    [string]$ZipPath = '',
    [string]$ChecksumPath = '',
    [ValidateRange(5,120)][int]$TimeoutSeconds = 20
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ZipPath)) { $ZipPath = Join-Path $projectRoot 'dist\Yourself-Copilot-windows-x64.zip' }
if ([string]::IsNullOrWhiteSpace($ChecksumPath)) { $ChecksumPath = Join-Path $projectRoot 'dist\SHA256SUMS.txt' }
$ZipPath = (Resolve-Path -LiteralPath $ZipPath).ProviderPath
$ChecksumPath = (Resolve-Path -LiteralPath $ChecksumPath).ProviderPath
$testRoot = Join-Path $projectRoot ('.qa\release-acceptance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$reportPath = Join-Path $testRoot 'acceptance.json'
$checks = New-Object 'Collections.Generic.List[string]'
$processResults = New-Object 'Collections.Generic.List[object]'
$archiveHash = ''
$version = [IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()

function Assert-Package([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw ('FAIL: ' + $Description) }
    $checks.Add($Description)
}

function Invoke-PackageTest([string]$Executable, [ValidateSet('--smoke-test','--verify-workboard')][string]$Mode) {
    # Both modes branch before Program.Main's named single-instance mutex.
    # Never run the ordinary entry point: that would activate the installed app.
    $testProcess = Start-Process -FilePath $Executable -ArgumentList $Mode -WorkingDirectory (Split-Path $Executable -Parent) -WindowStyle Hidden -PassThru
    try {
        if (-not $testProcess.WaitForExit($TimeoutSeconds * 1000)) {
            # This object refers only to the process created above, never a name lookup.
            $testProcess.Kill()
            $testProcess.WaitForExit(5000) | Out-Null
            throw ('Timed out running the isolated package: ' + $Mode)
        }
        $testProcess.Refresh()
        $processResults.Add([pscustomobject]@{ Mode=$Mode; Pid=$testProcess.Id; ExitCode=$testProcess.ExitCode })
        Assert-Package ($testProcess.ExitCode -eq 0) ($Mode + ' exits successfully')
    }
    finally { $testProcess.Dispose() }
}

try {
    $archiveHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines = @([IO.File]::ReadAllLines($ChecksumPath) | Where-Object { $_.Trim().Length -gt 0 })
    $matchingChecksums = @($checksumLines | Where-Object {
        $_ -match '^([a-fA-F0-9]{64})\s+\*?(.+)$' -and $Matches[2] -eq [IO.Path]::GetFileName($ZipPath)
    })
    Assert-Package ($matchingChecksums.Count -eq 1) 'checksum file has exactly one entry for the archive'
    $expectedHash = ($matchingChecksums[0] -split '\s+',2)[0].ToLowerInvariant()
    Assert-Package ($archiveHash -eq $expectedHash) 'ZIP SHA-256 matches the checksum file'

    # Deliberately explicit: a directory-wide copy could accidentally publish data.
    $expectedFiles = @(
        'Yourself Copilot.exe','WorkboardView.xaml','CompactWindow.xaml',
        'README.txt','THIRD-PARTY-NOTICES.txt','VERSION.txt',
        'fonts/font-assets-manifest.json','fonts/README.txt',
        'fonts/LXGWWenKai/COPYRIGHT.txt','fonts/LXGWWenKai/LXGWWenKai-Regular.ttf','fonts/LXGWWenKai/OFL.txt',
        'fonts/NotoSansSC/COPYRIGHT.txt','fonts/NotoSansSC/NotoSansSC-Regular.otf','fonts/NotoSansSC/OFL.txt',
        'icons/LICENSE','icons/manifest.json','icons/README.txt',
        'icons/brand/companion.ico','icons/brand/companion.svg','icons/brand/README.md',
        'icons/svg/ic_fluent_add_24_regular.svg','icons/svg/ic_fluent_arrow_expand_24_regular.svg',
        'icons/svg/ic_fluent_calendar_ltr_24_regular.svg','icons/svg/ic_fluent_checkbox_checked_24_regular.svg',
        'icons/svg/ic_fluent_dismiss_24_regular.svg','icons/svg/ic_fluent_history_24_regular.svg',
        'icons/svg/ic_fluent_image_multiple_24_regular.svg','icons/svg/ic_fluent_more_horizontal_24_regular.svg',
        'icons/svg/ic_fluent_note_24_regular.svg','icons/svg/ic_fluent_settings_24_regular.svg'
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace('\','/')
            Assert-Package ($entryName.StartsWith('Yourself-Copilot/',[StringComparison]::Ordinal)) ('archive entry stays in the application folder: ' + $entryName)
            $relativeName = $entryName.Substring('Yourself-Copilot/'.Length)
            Assert-Package ($expectedFiles -ccontains $relativeName) ('archive entry is on the public payload allowlist: ' + $relativeName)
            Assert-Package (-not $seen.ContainsKey($relativeName)) ('archive entry is unique: ' + $relativeName)
            $seen[$relativeName] = $true
        }
        Assert-Package ($seen.Count -eq $expectedFiles.Count) 'archive contains exactly 30 reviewed files and no user data or source files'
    }
    finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($ZipPath,$testRoot)
    $runtimeRoot = Join-Path $testRoot 'Yourself-Copilot'
    $executable = Join-Path $runtimeRoot 'Yourself Copilot.exe'

    $resourceHashes = @{}
    foreach ($relativeName in $expectedFiles) {
        $filePath = Join-Path $runtimeRoot $relativeName
        $resourceHashes[$relativeName] = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
        if ($relativeName -eq 'Yourself Copilot.exe') { $sourcePath = Join-Path $projectRoot 'dist\Yourself-Copilot\Yourself Copilot.exe' }
        elseif ($relativeName -like '*.xaml') { $sourcePath = Join-Path $projectRoot ('src\' + $relativeName) }
        elseif ($relativeName -like 'fonts/*' -or $relativeName -like 'icons/*') { $sourcePath = Join-Path $projectRoot ('assets\' + $relativeName) }
        else { continue }
        Assert-Package ($resourceHashes[$relativeName] -eq (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash) ('packaged runtime/resource bytes match the reviewed build: ' + $relativeName)
    }
    foreach ($document in @(@('README.txt','PORTABLE.txt'),@('THIRD-PARTY-NOTICES.txt','THIRD-PARTY-NOTICES.txt'))) {
        Assert-Package ([IO.File]::ReadAllText((Join-Path $runtimeRoot $document[0])) -eq [IO.File]::ReadAllText((Join-Path $projectRoot ('docs\' + $document[1])))) ('packaged documentation matches: ' + $document[0])
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    Assert-Package ([IO.File]::ReadAllText((Join-Path $runtimeRoot 'VERSION.txt')).Trim() -eq $version) 'package VERSION matches the repository'
    Assert-Package ($versionInfo.FileVersion -eq ($version + '.0')) 'EXE file version matches the release'
    Assert-Package ($versionInfo.ProductVersion -eq $version) 'EXE product version matches the release'
    Assert-Package ([Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString() -eq ($version + '.0')) 'EXE assembly version matches the release'

    Invoke-PackageTest $executable '--smoke-test'
    Assert-Package ([IO.File]::ReadAllText((Join-Path $runtimeRoot 'verification-ui.txt')).StartsWith('PASS:')) 'real desktop card and image manager load from the extracted package'
    $workboard = [IO.File]::ReadAllText((Join-Path $runtimeRoot 'workboard.json')) | ConvertFrom-Json
    $library = [IO.File]::ReadAllText((Join-Path $runtimeRoot 'library.json')) | ConvertFrom-Json
    Assert-Package (@($workboard.Tasks).Count -eq 0) 'first-run tasks are empty'
    Assert-Package (@($workboard.Removed).Count -eq 0) 'first-run removed tasks are empty'
    Assert-Package ($workboard.Ideas -ceq '') 'first-run memo is empty'
    Assert-Package ($workboard.TaskDraft -ceq '') 'first-run task draft is empty'
    Assert-Package (@($library.Items).Count -eq 0) 'first-run image library is empty'
    Assert-Package (@($library.Archived).Count -eq 0) 'first-run archived image list is empty'
    Assert-Package (@(Get-ChildItem -LiteralPath (Join-Path $runtimeRoot 'images') -Force -File -Recurse).Count -eq 0) 'first-run image storage contains no files'
    Assert-Package (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot '运行日志.txt'))) 'first-run resource loading produces no error log'

    Invoke-PackageTest $executable '--verify-workboard'
    $workboardReport = [IO.File]::ReadAllText((Join-Path $runtimeRoot 'verification-workboard-v3.txt'))
    Assert-Package ($workboardReport -match '(?m)^PASS:' -and $workboardReport -notmatch '(?m)^FAIL:') 'packaged workboard persistence and UI checks pass'
    foreach ($relativeName in $expectedFiles) {
        Assert-Package ($resourceHashes[$relativeName] -eq (Get-FileHash -LiteralPath (Join-Path $runtimeRoot $relativeName) -Algorithm SHA256).Hash) ('test execution preserves packaged resource bytes: ' + $relativeName)
    }
    $result = [pscustomobject]@{ Passed=$true; Version=$version; FileVersion=$versionInfo.FileVersion; ProductVersion=$versionInfo.ProductVersion; SHA256=$archiveHash; PayloadFiles=$expectedFiles.Count; AssertionCount=$checks.Count; Processes=$processResults.ToArray(); Checks=$checks.ToArray(); TestRoot=$testRoot }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Output ('PASS: ' + $checks.Count + ' release-package assertions; ' + $expectedFiles.Count + ' files; v' + $version)
    Write-Output ('SHA256: ' + $archiveHash)
    Write-Output ('Report: ' + $reportPath)
}
catch {
    [pscustomobject]@{ Passed=$false; Version=$version; SHA256=$archiveHash; AssertionCount=$checks.Count; Processes=$processResults.ToArray(); Checks=$checks.ToArray(); Error=$_.Exception.Message; TestRoot=$testRoot } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    throw
}
