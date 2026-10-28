#!/usr/bin/env bash
# ClaimFlow — start the backend WITHOUT Kafka (HTTP fan-out transport). No broker required.
# Usage:  ./scripts/start.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
export CLAIMFLOW_CONFIG="$(pwd)/config/claimflow.shared.json"
export Messaging__Provider=Http

mkdir -p logs .run data

echo "── ClaimFlow · starting backend (HTTP event transport, no Kafka) ──"

command -v dotnet >/dev/null || { echo "✗ .NET SDK not found. Install .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0"; exit 1; }
DOTNET_VER=$(dotnet --version)
case "$DOTNET_VER" in 9.*) ;; *) echo "⚠ Found .NET SDK $DOTNET_VER — this project targets .NET 9. It will likely still build, but 9.x is what's tested." ;; esac

echo "→ Restoring & building (first run downloads NuGet packages — this can take a minute)…"
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
  name="${SERVICES[$i]}"; port="${PORTS[$i]}"
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
  echo "All services are up. Messaging provider: HTTP fan-out (no broker)."
  echo "Gateway:        http://localhost:5100"
  echo "Health check:   http://localhost:5100/api/system/health"
  echo "Demo accounts:  http://localhost:5100/api/identity/demo-users  (password: Demo@123)"
  echo
  echo "Next: start the frontend with ./scripts/dev-web.sh (or see README for production build)."
  echo "Stop everything with ./scripts/stop.sh"
else
  echo "Some services failed to start — check the logs/ folder. Run ./scripts/stop.sh before retrying."
  exit 1
fi
