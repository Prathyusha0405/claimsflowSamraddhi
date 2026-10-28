# Stops the local Kafka broker started by kafka-up.ps1. Data under .kafka-data is kept, so the next
# start-kafka.ps1 picks up where it left off; delete scripts\kafka\.kafka-data for a totally clean slate.
Set-Location $PSScriptRoot
$PidFile = Join-Path (Get-Location) "..\..\.run\kafka.pid"
if (Test-Path $PidFile) {
  $procId = Get-Content $PidFile
  Stop-Process -Id $procId -ErrorAction SilentlyContinue
  if ($?) { Write-Host "  OK  stopped Kafka (pid $procId)" } else { Write-Host "  .   Kafka was not running" }
  Remove-Item $PidFile -ErrorAction SilentlyContinue
} else {
  Write-Host "  .   Kafka was not running (no pid file)"
}
