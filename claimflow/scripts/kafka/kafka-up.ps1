# Downloads (once) and starts a single-node Apache Kafka broker in KRaft mode - i.e. Kafka's own
# built-in consensus, so there is no ZooKeeper and no Docker, just the Kafka distribution + a JRE.
# Safe to re-run: if the broker is already up on :9092 this does nothing.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$KafkaVersion = "3.8.0"
$ScalaVersion = "2.13"
$Dist = "kafka_$ScalaVersion-$KafkaVersion"
$HomeDir = Join-Path (Get-Location) ".kafka"
$DataDir = Join-Path (Get-Location) ".kafka-data"
$LogDir  = Join-Path (Get-Location) "..\..\logs"
$PidFile = Join-Path (Get-Location) "..\..\.run\kafka.pid"
New-Item -ItemType Directory -Force -Path $LogDir, (Split-Path $PidFile) | Out-Null

$portOpen = Test-NetConnection -ComputerName localhost -Port 9092 -WarningAction SilentlyContinue -InformationLevel Quiet
if ($portOpen) { Write-Host "  .   Kafka already running on localhost:9092 - reusing it."; exit 0 }

if (-not (Get-Command java -ErrorAction SilentlyContinue)) {
  Write-Host "X Java not found. Kafka needs a JRE/JDK 11+ (e.g. https://adoptium.net). This is the ONLY"
  Write-Host "  external dependency for Kafka mode - the .NET services need nothing extra."
  exit 1
}

$KafkaHome = Join-Path $HomeDir $Dist
if (-not (Test-Path $KafkaHome)) {
  Write-Host "-> Downloading Apache Kafka $KafkaVersion (first run only, ~100 MB)..."
  New-Item -ItemType Directory -Force -Path $HomeDir | Out-Null
  $Url = "https://downloads.apache.org/kafka/$KafkaVersion/$Dist.tgz"
  $TgzPath = Join-Path $HomeDir "$Dist.tgz"
  try { Invoke-WebRequest -Uri $Url -OutFile $TgzPath }
  catch {
    Write-Host "X Could not download Kafka from $Url"
    Write-Host "  Download it manually, extract it, and place it at: $KafkaHome"
    exit 1
  }
  tar -xzf $TgzPath -C $HomeDir
  Remove-Item $TgzPath
}

$Config = Join-Path $DataDir "server.properties"
$FormattedMarker = Join-Path $DataDir ".formatted"

if (-not (Test-Path $FormattedMarker)) {
  Write-Host "-> First run: formatting local Kafka storage (KRaft, single node)..."
  New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
  Copy-Item (Join-Path $KafkaHome "config\kraft\server.properties") $Config
  Add-Content $Config ""
  Add-Content $Config ("log.dirs=" + (Join-Path $DataDir "logs").Replace('\','/'))
  Add-Content $Config "listeners=PLAINTEXT://localhost:9092,CONTROLLER://localhost:9093"
  Add-Content $Config "advertised.listeners=PLAINTEXT://localhost:9092"
  $ClusterId = (& "$KafkaHome\bin\windows\kafka-storage.bat" random-uuid).Trim()
  & "$KafkaHome\bin\windows\kafka-storage.bat" format -t $ClusterId -c $Config | Out-Null
  New-Item -ItemType File -Force -Path $FormattedMarker | Out-Null
}

Write-Host "-> Starting local Kafka broker (KRaft, no ZooKeeper, no Docker)..."
$p = Start-Process "$KafkaHome\bin\windows\kafka-server-start.bat" -ArgumentList $Config `
      -RedirectStandardOutput (Join-Path $LogDir "kafka.log") -RedirectStandardError (Join-Path $LogDir "kafka.err.log") `
      -PassThru -WindowStyle Hidden
$p.Id | Out-File $PidFile

Write-Host "-> Waiting for Kafka to accept connections" -NoNewline
for ($t = 0; $t -lt 45; $t++) {
  $up = Test-NetConnection -ComputerName localhost -Port 9092 -WarningAction SilentlyContinue -InformationLevel Quiet
  if ($up) { Write-Host ""; Write-Host "  OK  Kafka is up on localhost:9092 (logs\kafka.log)"; exit 0 }
  Write-Host "." -NoNewline; Start-Sleep -Seconds 1
}
Write-Host ""
Write-Host "X Kafka did not come up in time - check logs\kafka.log"
exit 1
