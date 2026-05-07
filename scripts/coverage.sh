#!/usr/bin/env bash
# Roda todos os testes com cobertura agregada e gera relatorio HTML local.
# Uso:
#   ./scripts/coverage.sh             # roda e imprime sumario
#   ./scripts/coverage.sh --open      # abre o HTML no browser ao final (xdg-open/open)
#   ./scripts/coverage.sh --no-build  # pula o build

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

results="$root/TestResults"
rm -rf "$results"
mkdir -p "$results"

projects=(
  "EasyStock.Domain.Tests/EasyStock.Domain.Tests.csproj"
  "EasyStock.Application.Tests/EasyStock.Application.Tests.csproj"
  "EasyStock.ArchitectureTests/EasyStock.ArchitectureTests.csproj"
  "EasyStock.Api.UnitTests/EasyStock.Api.UnitTests.csproj"
  "EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj"
  "EasyStock.Infra.Postgre.IntegrationTests/EasyStock.Infra.Postgre.IntegrationTests.csproj"
)

skip_build=0
open_html=0
for arg in "$@"; do
  case "$arg" in
    --no-build) skip_build=1 ;;
    --open)     open_html=1 ;;
  esac
done

if [ "$skip_build" -eq 0 ]; then
  echo ">> Building solution (Release)..."
  dotnet build EasyStok.sln -c Release
fi

for p in "${projects[@]}"; do
  name="$(basename "$p" .csproj)"
  echo ""
  echo "==> $name"
  dotnet test "$p" -c Release --no-build \
    --collect:"XPlat Code Coverage" \
    --settings coverlet.runsettings \
    --results-directory "$results/$name"
done

if ! command -v reportgenerator >/dev/null 2>&1; then
  echo ">> Installing ReportGenerator (global tool)..."
  dotnet tool install --global dotnet-reportgenerator-globaltool
  export PATH="$PATH:$HOME/.dotnet/tools"
fi

echo ""
echo ">> Generating aggregated report..."
reportgenerator \
  -reports:"$results/**/coverage.cobertura.xml" \
  -targetdir:"$results/CoverageReport" \
  -reporttypes:"Html;Cobertura;TextSummary;MarkdownSummary" \
  -classfilters:"-System.*;-Microsoft.*"

echo ""
echo "----------------- COVERAGE SUMMARY -----------------"
cat "$results/CoverageReport/Summary.txt"
echo "----------------------------------------------------"
echo ""
echo "HTML: $results/CoverageReport/index.html"

if [ "$open_html" -eq 1 ]; then
  if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$results/CoverageReport/index.html"
  elif command -v open >/dev/null 2>&1; then
    open "$results/CoverageReport/index.html"
  fi
fi
