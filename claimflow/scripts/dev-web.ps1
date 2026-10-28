# Runs the Angular app in dev mode with hot reload, proxied to the gateway on :5100.
# Start the backend first with .\scripts\start.ps1 (or start-kafka.ps1).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..\web\claimflow-web")

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
  Write-Host "X Node.js not found. Install Node 20.19+ / 22.12+: https://nodejs.org"; exit 1
}
if (-not (Test-Path node_modules)) {
  Write-Host "-> Installing frontend dependencies (first run only)..."
  npm install
}
Write-Host "-> Starting Angular dev server on http://localhost:4200 (proxying /api to http://localhost:5100)..."
npm start
