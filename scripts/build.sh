#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

reference_path="${StackmasterReferencePath:-$repo_root/lib/local/StackmasterReferences}"
reference_path="$(python3 ./scripts/verify_references.py "$reference_path" | tail -n 1)"
export StackmasterReferencePath="$reference_path"

./scripts/dotnet.sh restore Stackmaster.sln --locked-mode
./scripts/dotnet.sh build Stackmaster.sln --configuration Release --no-restore
./scripts/dotnet.sh run --project tests/Stackmaster.Tests/Stackmaster.Tests.csproj --configuration Release --no-build
./scripts/dotnet.sh run --project tests/Stackmaster.Compatibility.Tests/Stackmaster.Compatibility.Tests.csproj --configuration Release --no-build -- "$reference_path/assembly_valheim.dll"
python3 -m unittest discover -s tests -p 'test_*.py' -v
