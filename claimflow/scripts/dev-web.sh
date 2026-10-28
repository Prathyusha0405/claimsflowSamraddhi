#!/usr/bin/env bash
# Runs the Angular app in dev mode with hot reload, proxied to the gateway on :5100.
# Start the backend first with ./scripts/start.sh (or start-kafka.sh).
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../web/claimflow-web"

command -v node >/dev/null || { echo "✗ Node.js not found. Install Node 20.19+ / 22.12+: https://nodejs.org"; exit 1; }

if [ ! -d node_modules ]; then
  echo "→ Installing frontend dependencies (first run only)…"
  npm install
fi

echo "→ Starting Angular dev server on http://localhost:4200 (proxying /api to http://localhost:5100)…"
npm start
