#requires -Version 5.1
<#
.SYNOPSIS
    Encerra o ambiente local do EasyStok subido pelo up.ps1.

.DESCRIPTION
    Mata os processos `dotnet watch` (API/Web/Admin) lendo os PIDs em
    .build/local-env/<svc>.pid e derrubando a arvore inteira (taskkill /T),
    ja que o watch gera processos filhos (dotnet run -> app).

    NAO toca no Postgres: ele e o container pg-easystok que voce gerencia no WSL.
    Para para-lo, use o Docker no WSL (ex.: wsl -e docker stop pg-easystok).

.PARAMETER Sweep
    Alem dos .pid, varre e mata processos `dotnet watch` ORFAOS deste repo
    (command line com "dotnet ... watch" + path do repo). Orfaos nascem quando o
    .pid e sobrescrito por um relancamento (issue #738) — sem -Sweep eles sao
    invisiveis para este script e acumulam locks MSB3021 + RAM.

.EXAMPLE
    pwsh scripts/local-env/down.ps1
    pwsh scripts/local-env/down.ps1 -Sweep
#>
[CmdletBinding()]
param(
    [switch]$Sweep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$StateDir = Join-Path $RepoRoot '.build\local-env'

foreach ($name in @('api', 'web', 'admin')) {
    $pidFile = Join-Path $StateDir ("{0}.pid" -f $name)
    if (-not (Test-Path $pidFile)) {
        Write-Host "  $name : sem .pid (nao estava de pe?)" -ForegroundColor DarkGray
        continue
    }
    $processId = (Get-Content $pidFile -Raw).Trim()
    if ($processId -and (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
        taskkill /PID $processId /T /F *> $null
        Write-Host "  $name : encerrado (PID $processId)." -ForegroundColor Green
    } else {
        Write-Host "  $name : processo $processId ja nao existe." -ForegroundColor DarkGray
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

if ($Sweep) {
    # Orfaos: dotnet watch deste repo cujo PID nao estava (mais) em nenhum .pid.
    # Match pela command line: "watch" + (path do repo OU projeto EasyStock.*).
    # O caso comum e o segundo: o up.ps1 lanca com --project relativo
    # ("dotnet watch --project EasyStock.Api run"), sem o path do repo na linha.
    $repoPattern = [regex]::Escape($RepoRoot)
    $orphans = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -match '\bwatch\b' -and
                       ($_.CommandLine -match $repoPattern -or $_.CommandLine -match 'EasyStock\.') }
    if ($orphans) {
        foreach ($p in $orphans) {
            # /T pode ja ter derrubado este PID como filho de um anterior; e o
            # stderr de exe nativo redirecionado no PS 5.1 vira erro terminante —
            # dai o guard + cmd /c.
            if (-not (Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue)) { continue }
            cmd /c "taskkill /PID $($p.ProcessId) /T /F >nul 2>&1"
            Write-Host ("  sweep : dotnet watch orfao encerrado (PID {0})." -f $p.ProcessId) -ForegroundColor Green
        }
    } else {
        Write-Host '  sweep : nenhum dotnet watch orfao deste repo.' -ForegroundColor DarkGray
    }
}

Write-Host '  postgres : intacto (container pg-easystok no WSL; pare via wsl -e docker stop pg-easystok).' -ForegroundColor DarkGray
