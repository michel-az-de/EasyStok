#!/usr/bin/env pwsh
# Roda todos os testes com cobertura agregada e gera relatorio HTML local.
# Uso:
#   pwsh scripts/coverage.ps1            # roda e imprime sumario
#   pwsh scripts/coverage.ps1 --open     # abre o HTML no browser ao final
#   pwsh scripts/coverage.ps1 --no-build # pula o build (assume artefatos prontos)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $results = Join-Path $root 'TestResults'
    if (Test-Path $results) { Remove-Item -Recurse -Force $results }
    New-Item -ItemType Directory -Path $results | Out-Null

    $projects = @(
        'EasyStock.Domain.Tests/EasyStock.Domain.Tests.csproj',
        'EasyStock.Application.Tests/EasyStock.Application.Tests.csproj',
        'EasyStock.ArchitectureTests/EasyStock.ArchitectureTests.csproj',
        'EasyStock.Api.UnitTests/EasyStock.Api.UnitTests.csproj',
        'EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj',
        'EasyStock.Infra.Postgre.IntegrationTests/EasyStock.Infra.Postgre.IntegrationTests.csproj'
    )

    if ($args -notcontains '--no-build') {
        Write-Host 'Building solution (Release)...' -ForegroundColor Cyan
        dotnet build EasyStok.sln -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Build falhou.' }
    }

    foreach ($p in $projects) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($p)
        Write-Host ''
        Write-Host "==> $name" -ForegroundColor Cyan
        dotnet test $p -c Release --no-build `
            --collect:"XPlat Code Coverage" `
            --settings coverlet.runsettings `
            --results-directory (Join-Path $results $name)
    }

    if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
        Write-Host 'Installing ReportGenerator (global tool)...' -ForegroundColor Cyan
        dotnet tool install --global dotnet-reportgenerator-globaltool
    }

    Write-Host ''
    Write-Host 'Generating aggregated report...' -ForegroundColor Cyan
    reportgenerator `
        -reports:"$results/**/coverage.cobertura.xml" `
        -targetdir:"$results/CoverageReport" `
        -reporttypes:'Html;Cobertura;TextSummary;MarkdownSummary' `
        -classfilters:'-System.*;-Microsoft.*'

    Write-Host ''
    Write-Host '----------------- COVERAGE SUMMARY -----------------' -ForegroundColor Yellow
    Get-Content (Join-Path $results 'CoverageReport/Summary.txt')
    Write-Host '----------------------------------------------------' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "HTML: $results/CoverageReport/index.html" -ForegroundColor Green

    if ($args -contains '--open') {
        Start-Process (Join-Path $results 'CoverageReport/index.html')
    }
}
finally {
    Pop-Location
}
