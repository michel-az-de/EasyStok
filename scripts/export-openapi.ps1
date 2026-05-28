# Exporta o openapi.json da EasyStock.Api para docs/api/openapi.json.
#
# Usa Swashbuckle.AspNetCore.Cli (instalado como dotnet local tool em
# .config/dotnet-tools.json). A CLI carrega o assembly e dispara o pipeline
# de Swagger sem precisar subir o servidor HTTP — apenas executa o builder
# ate antes de app.Run(). Por isso o script precisa definir as env vars
# minimas exigidas pelos validators de startup (JWT, Jwt:Issuer/Audience,
# Database provider). Tudo fake/local — nada toca prod ou banco real.
#
# Uso:
#   pwsh -File scripts/export-openapi.ps1
#
# Saida:
#   docs/api/openapi.json (versionado no repo)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'EasyStock.Api/EasyStock.Api.csproj'
$apiDll     = Join-Path $repoRoot 'EasyStock.Api/bin/Debug/net9.0/EasyStock.Api.dll'
$outputDir  = Join-Path $repoRoot 'docs/api'
$outputFile = Join-Path $outputDir 'openapi.json'

Write-Host "[1/3] dotnet tool restore" -ForegroundColor Cyan
Push-Location $repoRoot
try {
    dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore falhou (exit $LASTEXITCODE)" }

    Write-Host "[2/3] dotnet build EasyStock.Api" -ForegroundColor Cyan
    dotnet build $apiProject --nologo --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou (exit $LASTEXITCODE)" }

    if (-not (Test-Path $apiDll)) { throw "Assembly nao encontrado: $apiDll" }
    if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

    Write-Host "[3/3] dotnet swagger tofile -> docs/api/openapi.json" -ForegroundColor Cyan

    # Env vars minimas pros validators de startup do Program.cs.
    # Fake/local — Swashbuckle CLI nao chega a abrir socket nem a tocar banco.
    # OPENAPI_EXPORT=true sinaliza ao Program.cs pra retornar antes de app.Run(),
    # evitando Host.StartAsync e os hosted services Postgres-only.
    # Staging desliga DI ValidateOnBuild (default so em Development) e evita
    # as fail-fasts Production-only do Program.cs. Swagger continua habilitado
    # (Program.cs:753-755: app.Environment.IsStaging() habilita Swagger).
    $env:OPENAPI_EXPORT                     = 'true'
    $env:ASPNETCORE_ENVIRONMENT             = 'Staging'
    $env:Jwt__SecretKey                     = 'openapi-export-fake-secret-key-32chars-min-len-ok'
    $env:Jwt__Issuer                        = 'easystock-openapi-export'
    $env:Jwt__Audience                      = 'easystock-openapi-export'
    $env:Database__Provider                 = 'Sqlite'
    $env:ConnectionStrings__SqliteConnection = 'Data Source=:memory:'
    $env:RunMigrationsOnStartup             = 'false'
    $env:Cors__AllowedOrigins__0            = 'http://localhost'

    dotnet swagger tofile --output $outputFile $apiDll v1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet swagger tofile falhou (exit $LASTEXITCODE)" }

    $size = (Get-Item $outputFile).Length
    Write-Host ""
    Write-Host "[OK] openapi.json gerado: $outputFile ($size bytes)" -ForegroundColor Green
}
finally {
    Pop-Location
}
