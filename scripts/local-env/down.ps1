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

.EXAMPLE
    pwsh scripts/local-env/down.ps1
#>
[CmdletBinding()]
param()

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

Write-Host '  postgres : intacto (container pg-easystok no WSL; pare via wsl -e docker stop pg-easystok).' -ForegroundColor DarkGray
