#!/usr/bin/env bash
# ClaimFlow — start the backend WITH Kafka as the event transport.
# Brings up a local, single-node Apache Kafka (KRaft mode — no Zookeeper, no Docker) first,
# then starts every service with Messaging:Provider=Kafka.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

echo "── ClaimFlow · starting backend (Kafka event transport) ──"
./scripts/kafka/kafka-up.sh

export CLAIMFLOW_CONFIG="$(pwd)/config/claimflow.shared.json"
export Messaging__Provider=Kafka
mkdir -p logs .run data

command -v dotnet >/dev/null || { echo "✗ .NET SDK not found. Install .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0"; exit 1; }
echo "→ Restoring & building…"
dotnet build ClaimFlow.sln -c Release -nologo -v quiet

SERVICES=(Identity Policy Notifications Reporting Claims Gateway)
PORTS=(5101 5102 5104 5105 5103 5100)

for i in "${!SERVICES[@]}"; do
  name="${SERVICES[$i]}"
  echo "→ Starting $name…"
  ( cd "src/Services/ClaimFlow.$name" && \
    nohup dotnet bin/Release/net9.0/ClaimFlow.$name.dll \
      > "../../../logs/$name.log" 2>&1 & echo $! > "../../../.run/$name.pid" )
done

echo -n "→ Waiting for services to become healthy"
for i in "${!SERVICES[@]}"; do
  port="${PORTS[$i]}"
  for _ in $(seq 1 60); do
    if curl -sf "http://localhost:$port/health" >/dev/null 2>&1; then echo -n "."; break; fi
    sleep 1
  done
done
echo

ok=true
for i in "${!SERVICES[@]}"; do
  name="${SERVICES[$i]}"; port="${PORTS[$i]}"
  if curl -sf "http://localhost:$port/health" >/dev/null 2>&1; then
    echo "  ✓ $name  (http://localhost:$port)"
  else
    echo "  ✗ $name did NOT come up — see logs/$name.log"; ok=false
  fi
done

echo
if $ok; then
  echo "All services are up. Messaging provider: Apache Kafka (localhost:9092, topic claims.events)."
  echo "If Claims started before Kafka finished booting, its outbox will have buffered events and will"
  echo "drain automatically within a few seconds — check logs/Claims.log or GET /health on port 5103."
  echo
  echo "Gateway:        http://localhost:5100"
  echo "Demo accounts:  http://localhost:5100/api/identity/demo-users  (password: Demo@123)"
  echo
  echo "Next: start the frontend with ./scripts/dev-web.sh"
  echo "Stop everything with ./scripts/stop-kafka.sh (stops services AND the local Kafka broker)."
else
  echo "Some services failed to start — check the logs/ folder."
  exit 1
fi
