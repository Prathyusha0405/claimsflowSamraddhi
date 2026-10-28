#!/usr/bin/env bash
# Runs the xUnit test suite.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
dotnet test tests/ClaimFlow.Tests/ClaimFlow.Tests.csproj -nologo
