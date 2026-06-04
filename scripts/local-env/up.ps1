#requires -Version 5.1
<#
.SYNOPSIS
    Sobe o ambiente local automatico do EasyStok: API (+PWA), Web e Admin via
    `dotnet watch` (hot reload). O Postgres e REUSADO do WSL (container que voce
    ja mantem de pe) — o script nao gerencia Docker.

.DESCRIPTION
    Idempotente: usa a porta em LISTEN como fonte de verdade de "esta de pe".
    Cada app sobe destacado (Start-Process), com stdout/stderr em .build/local-env/
    e o PID gravado em .build/local-env/<svc>.pid (consumido pelo down.ps1).

    Topologia desta maquina: dotnet roda no Windows (hot reload nativo); o Docker
    vive no WSL2. O Postgres e o container pg-easystok (db easystok_demo), exposto
    em localhost:5432. O script so detecta a 5432 — quem sobe/para o Postgres e voce.

    Portas (https): API 7039, Web 7010, Admin 7002.

    Chamado pelo hook .husky/pre-push com -Ensure: lanca o que faltar e retorna
    NA HORA (nao espera health), para nunca travar o push. Sem -Ensure (uso
    manual) aguarda a API responder e imprime um resumo com URLs e credenciais.

.PARAMETER Ensure
    Modo hook: nao espera health, output minimo. Use no automatico.

.EXAMPLE
    pwsh scripts/local-env/up.ps1
    pwsh scripts/local-env/up.ps1 -Ensure
#>
[CmdletBinding()]
param(
    [switch]$Ensure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Caminhos ────────────────────────────────────────────────────────────────
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$StateDir = Join-Path $RepoRoot '.build\local-env'
if (-not (Test-Path $StateDir)) { New-Item -ItemType Directory -Path $StateDir -Force | Out-Null }

function Write-Step($msg)  { if (-not $Ensure) { Write-Host "  $msg" -ForegroundColor Cyan } }
function Write-Ok($msg)    { if (-not $Ensure) { Write-Host "  OK  $msg" -ForegroundColor Green } }
function Write-Warn2($msg) { Write-Host "  AVISO  $msg" -ForegroundColor Yellow }

function Test-PortListening([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

# ── Servicos ────────────────────────────────────────────────────────────────
# Postgres reusado do WSL (pg-easystok / easystok_demo). Connection string e JWT
# podem ser sobrescritos por env var ja presente no shell.
$pgConn = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection')
if ([string]::IsNullOrWhiteSpace($pgConn)) {
    $pgConn = 'Host=localhost;Port=5432;Database=easystok_demo;Username=easystok;Password=easystok;SSL Mode=Disable'
}
$jwtKey = [Environment]::GetEnvironmentVariable('Jwt__SecretKey')
if ([string]::IsNullOrWhiteSpace($jwtKey)) {
    $jwtKey = 'EasyStock-Local-Dev-Secret-Key-0123456789abcdef'  # >= 32 chars (dev)
}

$Services = @(
    [pscustomobject]@{
        Name = 'api'; Project = 'EasyStock.Api'; Https = 7039; Http = 5280
        Env = @{
            'ConnectionStrings__DefaultConnection' = $pgConn
            'ConnectionStrings__Redis'             = ''
            'Database__Provider'                   = 'PostgreSQL'
            'Jwt__SecretKey'                       = $jwtKey
        }
    }
    [pscustomobject]@{
        Name = 'web'; Project = 'EasyStock.Web'; Https = 7010; Http = 5128
        Env = @{}   # ApiSettings:BaseUrl ja vem do appsettings.Development.json (7039)
    }
    [pscustomobject]@{
        Name = 'admin'; Project = 'EasyStock.Admin'; Https = 7002; Http = 5002
        Env = @{
            # Corrige o appsettings do Admin que aponta para 7000/7001 inexistentes.
            'ApiBaseUrl'      = 'https://localhost:7039'
            'EasyStockWebUrl' = 'https://localhost:7010'
        }
    }
)

# ── Bootstrap (best-effort, non-fatal) ──────────────────────────────────────
# Dev cert (necessario para os endpoints https). So avisa, nao falha.
dotnet dev-certs https --check *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Warn2 'Dev cert HTTPS ausente/nao confiavel. Rode uma vez: dotnet dev-certs https --trust'
}

# ── Postgres: so detecta (voce gerencia no WSL) ─────────────────────────────
if (Test-PortListening 5432) {
    Write-Ok 'Postgres de pe (localhost:5432).'
} else {
    Write-Warn2 'Postgres NAO esta na 5432. Suba no WSL e rode de novo, ex.: wsl -e docker start pg-easystok'
}

# ── Apps (dotnet watch destacado) ───────────────────────────────────────────
function Start-WatchService($svc) {
    if (Test-PortListening $svc.Https) {
        Write-Ok ("{0} ja esta de pe (porta {1})." -f $svc.Name, $svc.Https)
        return
    }

    # Env comum a todos os watch.
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', ("https://localhost:{0};http://localhost:{1}" -f $svc.Https, $svc.Http), 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER', '1', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_WATCH_RESTART_ON_RUDE_EDIT', '1', 'Process')
    foreach ($k in $svc.Env.Keys) {
        [Environment]::SetEnvironmentVariable($k, $svc.Env[$k], 'Process')
    }

    $outLog = Join-Path $StateDir ("{0}.out.log" -f $svc.Name)
    $errLog = Join-Path $StateDir ("{0}.err.log" -f $svc.Name)
    $pidFile = Join-Path $StateDir ("{0}.pid" -f $svc.Name)

    Write-Step ("Lancando {0} (dotnet watch -> https://localhost:{1})..." -f $svc.Name, $svc.Https)
    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('watch', '--project', $svc.Project, 'run', '--no-launch-profile') `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError $errLog `
        -WindowStyle Hidden `
        -PassThru
    Set-Content -Path $pidFile -Value $proc.Id -Encoding ascii
    Write-Ok ("{0} iniciado (PID {1}). Log: .build/local-env/{0}.out.log" -f $svc.Name, $proc.Id)
}

foreach ($svc in $Services) { Start-WatchService $svc }

# ── Modo hook: retorna ja ────────────────────────────────────────────────────
if ($Ensure) { return }

# ── Modo manual: aguarda a API e imprime resumo ─────────────────────────────
Write-Host ''
Write-Step 'Aguardando a API responder em https://localhost:7039 (1a vez aplica migrations, ~1 min)...'
$deadline = (Get-Date).AddSeconds(120)
$apiUp = $false
while ((Get-Date) -lt $deadline) {
    if (Test-PortListening 7039) { $apiUp = $true; break }
    Start-Sleep -Seconds 2
}
if ($apiUp) { Write-Ok 'API de pe.' } else { Write-Warn2 'API ainda nao subiu em 120s. Veja .build/local-env/api.err.log' }

Write-Host ''
Write-Host 'Ambiente local EasyStok' -ForegroundColor Magenta
Write-Host '  API + PWA : https://localhost:7039  (Swagger /swagger | PWA /pwa/ | health /health)'
Write-Host '  Web (MVC) : https://localhost:7010  (login em /auth/login)'
Write-Host '  Admin     : https://localhost:7002  (/ -> /Auth/Login)'
Write-Host '  Postgres  : localhost:5432  (pg-easystok @ WSL | db easystok_demo | easystok/easystok)'
Write-Host ''
Write-Host '  Credenciais de dev: definidas em EasyStock.Api/Data/SuperAdminSeed.cs' -ForegroundColor DarkGray
Write-Host '  (override por SEED_SUPERADMIN_EMAIL / SEED_SUPERADMIN_PASSWORD)' -ForegroundColor DarkGray
Write-Host '  Logs: .build/local-env/<svc>.out.log | Encerrar: scripts/local-env/down.ps1' -ForegroundColor DarkGray
