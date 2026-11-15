<#
Prova da #2653: a poda das pastas de build orfas do gate.

Cria, num TEMP isolado, pastas easystok-build-check-<hash> falsas e confere:
  - a pasta de um worktree vivo nunca e listada, mesmo antiga;
  - a orfa recente (menos de 24 h) nao e listada;
  - a orfa antiga e listada; sem -Apagar nada some; com -Apagar ela some;
  - pasta de outro nome no TEMP nao e tocada.

Uso: powershell -File scripts/poka-yoke/limpar-build-orfao.prova.ps1
#>
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script   = Join-Path $PSScriptRoot 'limpar-build-orfao.ps1'
. (Join-Path $PSScriptRoot 'build-out-dir.ps1')

$tempOriginal = $env:TEMP
$isolado = Join-Path ([IO.Path]::GetTempPath()) ("prova-2653-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $isolado | Out-Null
$env:TEMP = $isolado

$falhas = 0
function Confere([string]$descricao, [scriptblock]$teste) {
    try {
        if (& $teste) { Write-Host "ok    $descricao" }
        else { $script:falhas++; Write-Host "FALHA $descricao" }
    } catch { $script:falhas++; Write-Host "FALHA $descricao`n      $($_.Exception.Message)" }
}
function NovaPasta([string]$caminho, [int]$horasAtras) {
    New-Item -ItemType Directory -Path $caminho -Force | Out-Null
    Set-Content -Path (Join-Path $caminho 'EasyStock.Domain.dll') -Value 'x'
    (Get-Item $caminho).LastWriteTime = (Get-Date).AddHours(-$horasAtras)
    $caminho
}

try {
    $viva    = NovaPasta (Get-BuildOutDir $repoRoot) 72
    $antiga  = NovaPasta (Join-Path $isolado 'easystok-build-check-deadbeef') 48
    $recente = NovaPasta (Join-Path $isolado 'easystok-build-check-cafebabe') 1
    $outra   = NovaPasta (Join-Path $isolado 'outra-ferramenta') 999

    Confere 'o script de poda existe' { Test-Path $script }

    $listadas = @(& $script -RepoRoot $repoRoot 6>$null | ForEach-Object { $_.Path })
    Confere 'a orfa antiga e listada' { $listadas -contains $antiga }
    Confere 'a pasta do worktree vivo nao e listada, mesmo antiga' { $listadas -notcontains $viva }
    Confere 'a orfa recente nao e listada' { $listadas -notcontains $recente }
    Confere 'pasta de outro nome nao e listada' { $listadas -notcontains $outra }
    Confere 'sem -Apagar nada e apagado' { (Test-Path $antiga) -and (Test-Path $viva) -and (Test-Path $recente) }

    & $script -RepoRoot $repoRoot -Apagar 6>$null | Out-Null
    Confere 'com -Apagar a orfa antiga some' { -not (Test-Path $antiga) }
    Confere 'com -Apagar a viva, a recente e a de outro nome ficam' { (Test-Path $viva) -and (Test-Path $recente) -and (Test-Path $outra) }
} finally {
    $env:TEMP = $tempOriginal
    Remove-Item -LiteralPath $isolado -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "$falhas falha(s)"
exit ([int]($falhas -gt 0))
