#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workspace_root="$(cd "$repo_root/.." && pwd)"
local_dotnet="${DOTNET_ROOT:-$workspace_root/toolchains/dotnet-8}/dotnet"

if [[ ! -x "$local_dotnet" ]]; then
  printf 'Local .NET SDK not found at %s\n' "$local_dotnet" >&2
  printf 'Install it outside the repository under workspace/toolchains/dotnet-8.\n' >&2
  exit 1
fi

export DOTNET_ROOT="$(dirname "$local_dotnet")"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$workspace_root/.dotnet-cli-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$workspace_root/.nuget/packages}"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

exec "$local_dotnet" "$@"
