#!/usr/bin/env bash
# Stops every ClaimFlow service started by start.sh / start-kafka.sh (not the local Kafka broker — use stop-kafka.sh for that).
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

for name in Identity Policy Notifications Reporting Claims Gateway; do
  pidfile=".run/$name.pid"
  if [ -f "$pidfile" ]; then
    pid=$(cat "$pidfile")
    if kill "$pid" 2>/dev/null; then echo "  ✓ stopped $name (pid $pid)"; else echo "  · $name was not running"; fi
    rm -f "$pidfile"
  fi
done

# belt-and-braces: catch anything start.sh's pid files missed (e.g. a previous crashed run)
pkill -f "bin/Release/net9.0/ClaimFlow\.(Identity|Policy|Claims|Notifications|Reporting|Gateway)\.dll" 2>/dev/null || true
echo "ClaimFlow backend stopped."
