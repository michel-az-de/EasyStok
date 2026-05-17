#requires -Version 5.1
<#
.SYNOPSIS
    Substitui CACHE_VERSION nos service workers do PWA pelo SHA do commit.

.DESCRIPTION
    Roda no CI antes do build do container e do APK. Garante que o PWA servido
    em produção e o APK Capacitor reportem a mesma versão de bundle, que o
    PwaVersionProvider lê dinamicamente e expõe via /api/mobile/version.

    Atualiza:
      - EasyStock.Api/wwwroot/pwa/sw.js          (servido pela API)
      - casa-da-baba-mobile/apk/web/sw.js        (snapshot copiado para o APK)

    A pattern aceita CACHE_VERSION = 'qualquer-coisa' entre aspas simples.

.PARAMETER Sha
    SHA do commit (curto ou longo). Será prefixado com 'cdb-'.

.PARAMETER Root
    Raiz do repositório (default: dois níveis acima deste script).

.EXAMPLE
    pwsh ./scripts/rewrite-cache-version.ps1 -Sha $env:GITHUB_SHA

.EXAMPLE
    pwsh ./scripts/rewrite-cache-version.ps1 -Sha abc1234 -Root C:/rep/EasyStok
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Sha,

    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$short = ($Sha -replace '[^0-9a-fA-F]', '').Substring(0, [Math]::Min(12, $Sha.Length))
$newVersion = "cdb-$short"

$targets = @(
    (Join-Path $Root 'EasyStock.Api/wwwroot/pwa/sw.js'),
    (Join-Path $Root 'casa-da-baba-mobile/apk/web/sw.js')
)

$pattern = "const CACHE_VERSION = '[^']+';"
$replacement = "const CACHE_VERSION = '$newVersion';"

$updated = 0
foreach ($file in $targets) {
    if (-not (Test-Path $file)) {
        Write-Host "[skip] $file (não existe ainda)"
        continue
    }
    $content = Get-Content -Raw -Path $file
    if ($content -notmatch $pattern) {
        Write-Warning "[warn] padrão CACHE_VERSION não encontrado em $file"
        continue
    }
    # MatchEvaluator evita problemas com $ em string de replacement no PowerShell.
    $new = [regex]::Replace($content, $pattern, { param($m) "const CACHE_VERSION = '$newVersion';" })
    [System.IO.File]::WriteAllText($file, $new)
    $updated++
    Write-Host "[ok] $file -> $newVersion"
}

if ($updated -eq 0) {
    throw "Nenhum sw.js foi atualizado. Verifique paths e padrão CACHE_VERSION."
}

Write-Host ""
Write-Host "CACHE_VERSION = $newVersion aplicado em $updated arquivo(s)."
