# Stops the ClaimFlow services AND the local Kafka broker started by start-kafka.ps1.
Set-Location (Join-Path $PSScriptRoot "..")
& "$PSScriptRoot\stop.ps1"
& "$PSScriptRoot\kafka\kafka-down.ps1"
