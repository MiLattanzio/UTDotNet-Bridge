#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_dir"
dotnet restore UTDotNet.sln --locked-mode
dotnet test UTDotNet.sln --no-restore -c Release -m:1 -nodeReuse:false \
  -p:UseSharedCompilation=false -p:ContinuousIntegrationBuild=true --logger 'console;verbosity=minimal'
for package in Core Diagnostics UbuntuTouch Avalonia; do
  dotnet pack "src/UTDotNet.$package/UTDotNet.$package.csproj" \
    --no-build --no-restore -c Release -p:ContinuousIntegrationBuild=true -o artifacts/nuget
done
python3 scripts/verify-nuget.py artifacts/nuget
