# ClaimFlow — start the backend WITH Kafka as the event transport.
# Brings up a local, single-node Apache Kafka (KRaft mode - no ZooKeeper, no Docker) first,
# then starts every service with Messaging:Provider=Kafka.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host "-- ClaimFlow - starting backend (Kafka event transport) --"
& "$PSScriptRoot\kafka\kafka-up.ps1"

$env:CLAIMFLOW_CONFIG = Join-Path (Get-Location) "config\claimflow.shared.json"
$env:Messaging__Provider = "Kafka"
New-Item -ItemType Directory -Force -Path logs, .run, data | Out-Null

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Host "X .NET SDK not found. Install .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0"; exit 1
}
Write-Host "-> Restoring & building..."
dotnet build ClaimFlow.sln -c Release -nologo -v quiet

$services = @("Identity", "Policy", "Notifications", "Reporting", "Claims", "Gateway")
$ports    = @(5101, 5102, 5104, 5105, 5103, 5100)

for ($i = 0; $i -lt $services.Length; $i++) {
  $name = $services[$i]
  Write-Host "-> Starting $name..."
  $dll = "src\Services\ClaimFlow.$name\bin\Release\net9.0\ClaimFlow.$name.dll"
  $p = Start-Process dotnet -ArgumentList $dll -WorkingDirectory "src\Services\ClaimFlow.$name" `
        -RedirectStandardOutput "logs\$name.log" -RedirectStandardError "logs\$name.err.log" -PassThru -WindowStyle Hidden
  $p.Id | Out-File ".run\$name.pid"
}

Write-Host "-> Waiting for services to become healthy" -NoNewline
foreach ($port in $ports) {
  for ($t = 0; $t -lt 60; $t++) {
    try { Invoke-WebRequest -Uri "http://localhost:$port/health" -UseBasicParsing -TimeoutSec 1 | Out-Null; Write-Host "." -NoNewline; break }
    catch { Start-Sleep -Seconds 1 }
  }
}
Write-Host ""

$ok = $true
for ($i = 0; $i -lt $services.Length; $i++) {
  $name = $services[$i]; $port = $ports[$i]
  try { Invoke-WebRequest -Uri "http://localhost:$port/health" -UseBasicParsing -TimeoutSec 2 | Out-Null; Write-Host "  OK  $name  (http://localhost:$port)" }
  catch { Write-Host "  FAIL $name did not come up - see logs\$name.log"; $ok = $false }
}

Write-Host ""
if ($ok) {
  Write-Host "All services are up. Messaging provider: Apache Kafka (localhost:9092, topic claims.events)."
  Write-Host "Gateway:        http://localhost:5100"
  Write-Host "Demo accounts:  http://localhost:5100/api/identity/demo-users  (password: Demo@123)"
  Write-Host ""
  Write-Host "Next: start the frontend with .\scripts\dev-web.ps1"
  Write-Host "Stop everything with .\scripts\stop-kafka.ps1 (stops services AND the local Kafka broker)."
} else {
  Write-Host "Some services failed to start - check the logs\ folder."
  exit 1
}
