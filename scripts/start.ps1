param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'QTS\QTS.csproj'
$url = 'http://localhost:5088'
$healthUrl = "$url/api/health"
$proxyScript = Join-Path $PSScriptRoot 'okx-proxy.mjs'
$proxyUrl = 'http://127.0.0.1:5090'

function Test-OkxProxy {
    try {
        $result = Invoke-RestMethod -Uri "$proxyUrl/api/v5/public/time" -TimeoutSec 3
        return [bool]$result.data
    } catch {
        return $false
    }
}

function Start-OkxProxy {
    if (Test-OkxProxy) { return }
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) {
        Write-Host '[QTS] Node.js not found; OKX TLS proxy will not start.'
        return
    }
    Write-Host "[QTS] Starting local OKX TLS proxy on $proxyUrl ..."
    Start-Process -FilePath $node.Source -ArgumentList @($proxyScript) -WorkingDirectory $root -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $root 'build-tmp\okx-proxy.out.log') `
        -RedirectStandardError (Join-Path $root 'build-tmp\okx-proxy.err.log') | Out-Null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 250
        if (Test-OkxProxy) { return }
    }
    Write-Host '[QTS] WARNING: OKX TLS proxy did not become ready; real API requests may fail.'
}

function Test-QtsRunning {
    try {
        $result = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2
        return [bool]$result.success
    } catch {
        return $false
    }
}

Start-OkxProxy

if (Test-QtsRunning) {
    Write-Host "[QTS] Already running at $url"
    if (-not $NoBrowser) { Start-Process $url }
    exit 0
}

# Clean up a stale/half-started instance that may still lock the build output.
$stale = Get-Process -Name QTS -ErrorAction SilentlyContinue
if ($stale) {
    Write-Host '[QTS] Found stale QTS process(es), stopping them first ...'
    foreach ($proc in $stale) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 800
}

$portOwner = Get-NetTCPConnection -LocalPort 5088 -State Listen -ErrorAction SilentlyContinue
if ($portOwner) {
    Write-Host '[QTS] Port 5088 is already used by another process:'
    $portOwner | Format-Table -AutoSize
    Write-Host '[QTS] Run stop.bat, or free the port, then retry.'
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '[QTS] dotnet SDK not found in PATH.'
    exit 1
}

Write-Host '[QTS] Building backend ...'
dotnet build $project -v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host '[QTS] Build failed. See the errors above.'
    Write-Host '[QTS] If you see MSB3027 / file-in-use, close the old QTS window or run stop.bat first.'
    exit 1
}

if (-not $NoBrowser) {
    Start-Job -ScriptBlock {
        Start-Sleep -Seconds 4
        Start-Process 'http://localhost:5088'
    } | Out-Null
}

Write-Host "[QTS] Starting at $url"
Write-Host '[QTS] Press Ctrl+C to stop.'
dotnet run --project $project --no-build --urls $url
