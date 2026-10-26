param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$stopScript = Join-Path $PSScriptRoot 'stop.ps1'
$startScript = Join-Path $PSScriptRoot 'start.ps1'

& $stopScript
Start-Sleep -Seconds 1

if ($NoBrowser) {
    & $startScript -NoBrowser
} else {
    & $startScript
}
