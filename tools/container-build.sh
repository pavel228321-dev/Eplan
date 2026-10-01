#!/usr/bin/env bash
# Сборка EplanCipMvp в контейнере разработки (Linux + mono) против DLL EPLAN 2.9 из .uploads.
# Исходники не трогаем: копия в scratch, HintPath -> /workspace/.uploads/eplan-2.9-assemblies.
# Использование: tools/container-build.sh [selftest|all]
set -euo pipefail
MODE="${1:-selftest}"
SRC="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${BUILD_DIR:-/tmp/eplan-cip-build}"
rm -rf "$OUT" && mkdir -p "$OUT" && cp -r "$SRC" "$OUT/src"
sed -i 's#C:\\Program Files\\EPLAN\\Platform\\Bin\\#/workspace/.uploads/eplan-2.9-assemblies/#g' "$OUT"/src/*/*.csproj
export DOTNET_ROOT=/workspace/.dotnet PATH=/workspace/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$OUT/src"
if [ "$MODE" = "all" ]; then
  dotnet build EplanCipMvp.sln -c Release -m:1 -nologo -v q
else
  dotnet build EplanCipMvp.Core.SelfTest -c Release -m:1 -nologo -v q
fi
mono "$OUT/src/EplanCipMvp.Core.SelfTest/bin/Release/net48/EplanCipMvp.Core.SelfTest.exe"
