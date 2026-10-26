param([Parameter(Mandatory=$true)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.TestIdentity -notmatch '^YourselfCopilot\.QA\.[a-f0-9]{32}$') { throw 'Only an isolated test identity may be installed by this test.' }
$setup = (Resolve-Path -LiteralPath $manifest.Setup).ProviderPath
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne $manifest.SetupSha256) { throw 'Installer hash mismatch.' }
$root = Join-Path $projectRoot ('.qa\installer-acceptance-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $root '中文 & upgrade\Yourself Copilot'
New-Item -ItemType Directory -Path $app -Force | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $manifest.TestIdentity + '_is1'
$programShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($manifest.TestIdentity + '.lnk')
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($manifest.TestIdentity + '.lnk')
$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
function Check([bool]$Condition,[string]$Name) { if (-not $Condition) { throw ('FAIL: ' + $Name) }; $checks.Add($Name) }
function Run-Installer([string]$Executable,[string[]]$Arguments,[bool]$ShouldPass=$true) {
    $p = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $p.WaitForExit(45000)) { $p.Kill(); throw 'Isolated installer timed out.' }
        $p.Refresh()
        # Inno's original uninstaller exits before its temporary worker runs
        # usPostUninstall. Wait for the worker's log to close before assertions.
        if ([IO.Path]::GetFileName($Executable) -match '^unins\d+\.exe$') {
            $logArgument = @($Arguments | Where-Object { $_ -match '^/LOG="(.+)"$' })
            if ($logArgument.Count -ne 1) { throw 'Uninstaller completion log is required.' }
            $null = $logArgument[0] -match '^/LOG="(.+)"$'
            $completionLog = $Matches[1]
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            do {
                $finished = $false
                if (Test-Path -LiteralPath $completionLog) {
                    try {
                        $stream = [IO.File]::Open($completionLog,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
                        $reader = [IO.StreamReader]::new($stream)
                        try { $finished = $reader.ReadToEnd().Contains('Log closed.') } finally { $reader.Dispose() }
                    } catch [IO.IOException] { }
                }
                if (-not $finished) { Start-Sleep -Milliseconds 100 }
            } until ($finished -or [DateTime]::UtcNow -gt $deadline)
            if (-not $finished) { throw 'Uninstall worker did not complete.' }
        }
        if ($ShouldPass) { Check ($p.ExitCode -eq 0) ('process succeeds: ' + [IO.Path]::GetFileName($Executable)) }
        else { Check ($p.ExitCode -ne 0) 'unsafe install is rejected' }
    } finally { $p.Dispose() }
}
function Install-Args([string]$LogName,[bool]$Desktop=$false) {
    @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $app + '"'),('/LOG="' + (Join-Path $root $LogName) + '"'),
        $(if ($Desktop) { '/TASKS=desktopicon' } else { '/TASKS=' }))
}
$data = @{}
try {
    Check (-not (Test-Path -LiteralPath $registry)) 'test identity is separate from the installed product'
    Run-Installer $setup (Install-Args 'fresh.log')
    Check (Test-Path -LiteralPath $registry) 'Windows app registration exists for current user'
    Check (Test-Path -LiteralPath $programShortcut) 'Start menu shortcut exists'
    Check (-not (Test-Path -LiteralPath $desktopShortcut)) 'desktop shortcut is off by default'
    Check (-not (Test-Path -LiteralPath (Join-Path $app 'workboard.json'))) 'installation does not seed or overwrite personal data'
    foreach ($relative in $manifest.Files) {
        $installed = Join-Path $app $relative
        Check (Test-Path -LiteralPath $installed) ('installed: ' + $relative)
        Check ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath (Join-Path $manifest.Stage ('payload\' + $relative))).Hash) ('bytes verified: ' + $relative)
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($programShortcut)
    Check ($shortcut.TargetPath -eq (Join-Path $app 'Yourself Copilot.exe') -and $shortcut.WorkingDirectory -eq $app) 'shortcut targets the installed program and working folder'
    foreach ($mode in @('--smoke-test','--verify-workboard')) {
        Run-Installer (Join-Path $app 'Yourself Copilot.exe') @($mode)
    }
    # Neutral fixtures only; never read or change a user's live folder.
    foreach ($relative in @('workboard.json','workboard.json.bak','library.json','settings.json','images\keep.png','history\workboard\keep.json','recovery\keep.json','library-recovery-test.json')) {
        $path = Join-Path $app $relative
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path,('neutral fixture ' + $relative),[Text.UTF8Encoding]::new($false))
        $data[$relative] = (Get-FileHash -LiteralPath $path).Hash
    }
    Run-Installer $setup (Install-Args 'upgrade.log' $true)
    Check (Test-Path -LiteralPath $desktopShortcut) 'desktop shortcut can be selected on upgrade'
    foreach ($relative in $data.Keys) { Check ((Get-FileHash -LiteralPath (Join-Path $app $relative)).Hash -eq $data[$relative]) ('upgrade preserves: ' + $relative) }
    $mutex = [Threading.Mutex]::new($false,('Local\' + $manifest.TestIdentity))
    try { Run-Installer $setup (Install-Args 'running.log') $false } finally { $mutex.Dispose() }
    # Refuse downgrade based on file version, without touching a real app.
    $newer = Join-Path $root 'newer.exe'
    $source = Join-Path $root 'newer.cs'
    [IO.File]::WriteAllText($source,'using System.Reflection; [assembly: AssemblyFileVersion("99.0.0.0")] class P { static void Main() {} }')
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe ('/out:' + $newer) $source
    if ($LASTEXITCODE -ne 0) { throw 'Cannot compile downgrade fixture.' }
    $appExe = Join-Path $app 'Yourself Copilot.exe'
    Copy-Item -LiteralPath $newer -Destination $appExe -Force
    Run-Installer $setup (Install-Args 'downgrade.log') $false
    Check ((Get-FileHash -LiteralPath $appExe).Hash -eq (Get-FileHash -LiteralPath $newer).Hash) 'newer version remains unchanged after rejected downgrade'
    Copy-Item -LiteralPath (Join-Path $manifest.Stage 'payload\Yourself Copilot.exe') -Destination $appExe -Force
    $runKey.SetValue($manifest.TestIdentity,('"' + $appExe + '" --startup'))
    $runKey.Flush()
    Check ($runKey.GetValue($manifest.TestIdentity) -eq ('"' + $appExe + '" --startup')) 'matching startup fixture is written'
    Run-Installer (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall.log') + '"'))
    Check (-not (Test-Path -LiteralPath $appExe)) 'uninstall removes application executable'
    Check (-not (Test-Path -LiteralPath $registry)) 'uninstall removes Windows app registration'
    Check (-not (Test-Path -LiteralPath $programShortcut) -and -not (Test-Path -LiteralPath $desktopShortcut)) 'uninstall removes both owned shortcuts'
    Check ($null -eq $runKey.GetValue($manifest.TestIdentity)) 'uninstall removes its matching login startup entry'
    foreach ($relative in $data.Keys) { Check ((Get-FileHash -LiteralPath (Join-Path $app $relative)).Hash -eq $data[$relative]) ('uninstall preserves: ' + $relative) }
    Run-Installer $setup (Install-Args 'reinstall.log')
    foreach ($relative in $data.Keys) { Check ((Get-FileHash -LiteralPath (Join-Path $app $relative)).Hash -eq $data[$relative]) ('reinstall preserves: ' + $relative) }
    $runKey.SetValue($manifest.TestIdentity,'"D:\Other app\Yourself Copilot.exe" --startup')
    Run-Installer (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall-foreign.log') + '"'))
    Check ($runKey.GetValue($manifest.TestIdentity) -eq '"D:\Other app\Yourself Copilot.exe" --startup') 'uninstall preserves a startup entry pointing elsewhere'
    [pscustomobject]@{Passed=$true; Count=$checks.Count; Checks=@($checks.ToArray()); Manifest=$ManifestPath; TestRoot=$root} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'acceptance.json') -Encoding UTF8
    Write-Output ('PASS: ' + $checks.Count + ' installer checks; ' + $root)
}
finally { $runKey.DeleteValue($manifest.TestIdentity,$false); $runKey.Dispose() }
