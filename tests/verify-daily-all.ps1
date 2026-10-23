param([string]$ApplicationName = 'Yourself Copilot.exe')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'verify-daily-store.ps1')
& (Join-Path $PSScriptRoot 'verify-memo-history.ps1')
& (Join-Path $PSScriptRoot 'verify-memo-history-window.ps1')
& (Join-Path $PSScriptRoot 'verify-memo-paper-store.ps1')
& (Join-Path $PSScriptRoot 'verify-memo-paper-window.ps1')
& (Join-Path $PSScriptRoot 'verify-history-panel.ps1')
& (Join-Path $PSScriptRoot 'verify-memo-layout.ps1')
& (Join-Path $PSScriptRoot 'verify-startup.ps1')
& (Join-Path $PSScriptRoot 'verify-workbench.ps1')
& (Join-Path $PSScriptRoot 'verify-v3.ps1') -ApplicationName $ApplicationName
& (Join-Path $PSScriptRoot 'verify-daily.ps1') -ApplicationName $ApplicationName
