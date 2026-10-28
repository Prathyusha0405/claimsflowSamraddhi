#!/usr/bin/env bash
# Downloads (once) and starts a single-node Apache Kafka broker in KRaft mode — i.e. Kafka's own
# built-in consensus, so there is no ZooKeeper and no Docker, just the Kafka distribution + a JRE.
# Safe to re-run: if the broker is already up on :9092 this does nothing.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

KAFKA_VERSION="3.8.0"
SCALA_VERSION="2.13"
DIST="kafka_${SCALA_VERSION}-${KAFKA_VERSION}"
HOME_DIR="$(pwd)/.kafka"
DATA_DIR="$(pwd)/.kafka-data"
LOG_DIR="$(pwd)/../../logs"
PID_FILE="$(pwd)/../../.run/kafka.pid"
mkdir -p "$LOG_DIR" "$(dirname "$PID_FILE")"

if nc -z localhost 9092 2>/dev/null; then
  echo "  · Kafka already running on localhost:9092 — reusing it."
  exit 0
fi

command -v java >/dev/null || { echo "✗ Java not found. Kafka needs a JRE/JDK 11+ (e.g. https://adoptium.net). This is the ONLY external dependency for Kafka mode — the .NET services need nothing extra."; exit 1; }

if [ ! -d "$HOME_DIR/$DIST" ]; then
  echo "→ Downloading Apache Kafka $KAFKA_VERSION (first run only, ~100 MB)…"
  mkdir -p "$HOME_DIR"
  URL="https://downloads.apache.org/kafka/${KAFKA_VERSION}/${DIST}.tgz"
  curl -fL --progress-bar "$URL" -o "$HOME_DIR/${DIST}.tgz" || {
    echo "✗ Could not download Kafka from $URL"
    echo "  Download it manually, extract it, and place it at: $HOME_DIR/$DIST"
    exit 1
  }
  tar -xzf "$HOME_DIR/${DIST}.tgz" -C "$HOME_DIR"
  rm -f "$HOME_DIR/${DIST}.tgz"
fi

KAFKA_HOME="$HOME_DIR/$DIST"
CONFIG="$DATA_DIR/server.properties"

if [ ! -f "$DATA_DIR/.formatted" ]; then
  echo "→ First run: formatting local Kafka storage (KRaft, single node)…"
  mkdir -p "$DATA_DIR"
  cp "$KAFKA_HOME/config/kraft/server.properties" "$CONFIG"
  # single-node demo overrides: fixed broker/controller id, plain 9092 listener, data under our own folder
  {
    echo ""
    echo "log.dirs=$DATA_DIR/logs"
    echo "listeners=PLAINTEXT://localhost:9092,CONTROLLER://localhost:9093"
    echo "advertised.listeners=PLAINTEXT://localhost:9092"
  } >> "$CONFIG"
  CLUSTER_ID=$("$KAFKA_HOME/bin/kafka-storage.sh" random-uuid)
  "$KAFKA_HOME/bin/kafka-storage.sh" format -t "$CLUSTER_ID" -c "$CONFIG" >/dev/null
  touch "$DATA_DIR/.formatted"
fi

echo "→ Starting local Kafka broker (KRaft, no ZooKeeper, no Docker)…"
nohup "$KAFKA_HOME/bin/kafka-server-start.sh" "$CONFIG" > "$LOG_DIR/kafka.log" 2>&1 &
echo $! > "$PID_FILE"

echo -n "→ Waiting for Kafka to accept connections"
for _ in $(seq 1 45); do
  if nc -z localhost 9092 2>/dev/null; then echo; echo "  ✓ Kafka is up on localhost:9092 (logs/kafka.log)"; exit 0; fi
  echo -n "."; sleep 1
done
echo
echo "✗ Kafka did not come up in time — check logs/kafka.log"
exit 1
