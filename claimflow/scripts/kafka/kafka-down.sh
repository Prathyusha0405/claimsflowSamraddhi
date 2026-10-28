#!/usr/bin/env bash
# Stops the local Kafka broker started by kafka-up.sh. Data under .kafka-data is kept, so the next
# start-kafka.sh picks up where it left off; delete scripts/kafka/.kafka-data for a totally clean slate.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
PID_FILE="$(pwd)/../../.run/kafka.pid"
if [ -f "$PID_FILE" ]; then
  pid=$(cat "$PID_FILE")
  kill "$pid" 2>/dev/null && echo "  ✓ stopped Kafka (pid $pid)" || echo "  · Kafka was not running"
  rm -f "$PID_FILE"
else
  pkill -f "kafka\.Kafka" 2>/dev/null && echo "  ✓ stopped Kafka" || echo "  · Kafka was not running"
fi
