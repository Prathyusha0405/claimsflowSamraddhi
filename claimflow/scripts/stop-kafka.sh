#!/usr/bin/env bash
# Stops the ClaimFlow services AND the local Kafka broker started by start-kafka.sh.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
./scripts/stop.sh
./scripts/kafka/kafka-down.sh
