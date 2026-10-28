# Runs the xUnit test suite.
Set-Location (Join-Path $PSScriptRoot "..")
dotnet test tests\ClaimFlow.Tests\ClaimFlow.Tests.csproj -nologo
