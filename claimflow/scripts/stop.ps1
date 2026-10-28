# Stops every ClaimFlow service started by start.ps1 / start-kafka.ps1.
Set-Location (Join-Path $PSScriptRoot "..")
foreach ($name in @("Identity","Policy","Notifications","Reporting","Claims","Gateway")) {
  $pidFile = ".run\$name.pid"
  if (Test-Path $pidFile) {
    $procId = Get-Content $pidFile
    Stop-Process -Id $procId -ErrorAction SilentlyContinue
    if ($?) { Write-Host "  OK  stopped $name (pid $procId)" } else { Write-Host "  .   $name was not running" }
    Remove-Item $pidFile -ErrorAction SilentlyContinue
  }
}
# belt-and-braces: catch anything the pid files missed
Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object {
  $_.Path -and (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine -match "ClaimFlow\.(Identity|Policy|Claims|Notifications|Reporting|Gateway)\.dll"
} | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "ClaimFlow backend stopped."
