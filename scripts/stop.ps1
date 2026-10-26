param()

$ErrorActionPreference = 'SilentlyContinue'
$stopped = $false

$listeners = Get-NetTCPConnection -LocalPort 5088 -State Listen -ErrorAction SilentlyContinue
foreach ($procId in ($listeners | Select-Object -ExpandProperty OwningProcess -Unique)) {
    Write-Host "[QTS] Stopping listener PID $procId ..."
    Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
    $stopped = $true
}

Start-Sleep -Milliseconds 500

$qts = Get-Process -Name QTS -ErrorAction SilentlyContinue
foreach ($proc in $qts) {
    Write-Host "[QTS] Stopping QTS PID $($proc.Id) ..."
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    $stopped = $true
}

# Local OKX TLS proxy (Node) started by start.ps1.
$proxyProcesses = Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*okx-proxy.mjs*' }
foreach ($proc in $proxyProcesses) {
    Write-Host "[QTS] Stopping OKX proxy PID $($proc.ProcessId) ..."
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    $stopped = $true
}

if ($stopped) {
    Start-Sleep -Milliseconds 500
    Write-Host '[QTS] Stopped.'
} else {
    Write-Host '[QTS] No running QTS instance was found.'
}

$still = Get-NetTCPConnection -LocalPort 5088 -State Listen -ErrorAction SilentlyContinue
if ($still) {
    Write-Host '[QTS] WARNING: port 5088 is still in use:'
    $still | Format-Table -AutoSize
    exit 1
}
exit 0
