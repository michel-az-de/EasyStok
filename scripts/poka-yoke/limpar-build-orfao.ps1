<#
limpar-build-orfao -- poda as pastas de build do gate que ficaram sem worktree (issue 2653).

Cada worktree compila em %TEMP%\easystok-build-check-<hash> (issue 1117) e a pasta fica para
tras quando o worktree e removido. Por padrao SO LISTA (quantidade e GB). Apaga so com -Apagar.
Nunca toca a pasta de um worktree vivo nem orfa com escrita nas ultimas -Horas horas.

Uso: powershell -File scripts/poka-yoke/limpar-build-orfao.ps1 [-Horas 24] [-Apagar]
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [int]$Horas = 24,
    [switch]$Apagar
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-out-dir.ps1')

$orfas = @(Get-BuildOutDirOrfas $RepoRoot $Horas)
$bytes = 0
foreach ($p in $orfas) {
    $tam = (Get-ChildItem -LiteralPath $p.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    $bytes += [int64]$tam
    [PSCustomObject]@{ Path = $p.FullName; MB = [math]::Round($tam / 1MB); UltimaEscrita = $p.LastWriteTime }
}
$gb = [math]::Round($bytes / 1GB, 1)
if (-not $Apagar) {
    Write-Host "[build-orfao] $($orfas.Count) pasta(s) orfa(s), $gb GB. Nada apagado; rode com -Apagar para podar."
    return
}
$falhou = 0
foreach ($p in $orfas) {
    try { Remove-Item -LiteralPath $p.FullName -Recurse -Force -ErrorAction Stop }
    catch { $falhou++; Write-Host "[build-orfao] nao apagou $($p.Name): $($_.Exception.Message)" }
}
Write-Host "[build-orfao] $($orfas.Count - $falhou) de $($orfas.Count) pasta(s) apagada(s), ~$gb GB liberados."
