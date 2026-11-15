<#
Prova da #2653: a poda das pastas de build orfas do gate.

Cria, num TEMP isolado, pastas easystok-build-check-<hash> falsas e confere:
  - a pasta de um worktree vivo nunca e listada, mesmo antiga;
  - a orfa recente (menos de 24 h) nao e listada, nem a que so teve arquivo reescrito;
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
    $dll = Join-Path $caminho 'EasyStock.Domain.dll'
    Set-Content -Path $dll -Value 'x'
    (Get-Item $dll).LastWriteTime = (Get-Date).AddHours(-$horasAtras)
    (Get-Item $caminho).LastWriteTime = (Get-Date).AddHours(-$horasAtras)
    $caminho
}

try {
    $viva    = NovaPasta (Get-BuildOutDir $repoRoot) 72
    $antiga  = NovaPasta (Join-Path $isolado 'easystok-build-check-deadbeef') 48
    $recente = NovaPasta (Join-Path $isolado 'easystok-build-check-cafebabe') 1
    $outra   = NovaPasta (Join-Path $isolado 'outra-ferramenta') 999
    # Build incremental reescreve a DLL sem mudar a data da pasta: conta a escrita do arquivo.
    $emUso   = NovaPasta (Join-Path $isolado 'easystok-build-check-0ddba11a') 48
    (Get-Item (Join-Path $emUso 'EasyStock.Domain.dll')).LastWriteTime = (Get-Date).AddMinutes(-5)
    (Get-Item $emUso).LastWriteTime = (Get-Date).AddHours(-48)

    Confere 'o script de poda existe' { Test-Path $script }

    $listadas = @(& $script -RepoRoot $repoRoot 6>$null | ForEach-Object { $_.Path })
    Confere 'a orfa antiga e listada' { $listadas -contains $antiga }
    Confere 'a pasta do worktree vivo nao e listada, mesmo antiga' { $listadas -notcontains $viva }
    Confere 'a orfa recente nao e listada' { $listadas -notcontains $recente }
    Confere 'orfa com pasta antiga mas DLL reescrita ha 5 min nao e listada' { $listadas -notcontains $emUso }
    Confere 'pasta de outro nome nao e listada' { $listadas -notcontains $outra }
    Confere 'sem -Apagar nada e apagado' { (Test-Path $antiga) -and (Test-Path $viva) -and (Test-Path $recente) }

    & $script -RepoRoot $repoRoot -Apagar 6>$null | Out-Null
    Confere 'com -Apagar a orfa antiga some' { -not (Test-Path $antiga) }
    Confere 'com -Apagar a viva, a recente e a de outro nome ficam' { (Test-Path $viva) -and (Test-Path $recente) -and (Test-Path $outra) }

    # Juncao: nunca seguir. Alvos ficam FORA do TEMP isolado da poda, mas dentro da pasta da prova.
    $fora = Join-Path $isolado 'fora'
    $alvoTopo   = NovaPasta (Join-Path $fora 'alvo-topo') 72
    $alvoDentro = NovaPasta (Join-Path $fora 'alvo-dentro') 72
    $env:TEMP = Join-Path $isolado 'temp-juncao'
    New-Item -ItemType Directory -Path $env:TEMP | Out-Null
    $juncaoTopo = Join-Path $env:TEMP 'easystok-build-check-0badf00d'
    New-Item -ItemType Junction -Path $juncaoTopo -Target $alvoTopo | Out-Null
    $orfaComJuncao = NovaPasta (Join-Path $env:TEMP 'easystok-build-check-feedface') 0
    New-Item -ItemType Junction -Path (Join-Path $orfaComJuncao 'link') -Target $alvoDentro | Out-Null

    $listadas = @(& $script -RepoRoot $repoRoot -Horas 0 6>$null | ForEach-Object { $_.Path })
    Confere 'juncao no topo do TEMP nao e listada' { $listadas -notcontains $juncaoTopo }
    & $script -RepoRoot $repoRoot -Horas 0 -Apagar 6>$null | Out-Null
    Confere 'juncao no topo fica e o alvo dela fica intacto' { (Test-Path $juncaoTopo) -and (Test-Path (Join-Path $alvoTopo 'EasyStock.Domain.dll')) }
    Confere 'orfa com juncao dentro some sem apagar o alvo da juncao' { (-not (Test-Path $orfaComJuncao)) -and (Test-Path (Join-Path $alvoDentro 'EasyStock.Domain.dll')) }
} finally {
    Get-ChildItem -LiteralPath $isolado -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
        ForEach-Object { [IO.Directory]::Delete($_.FullName) }
    $env:TEMP = $tempOriginal
    Remove-Item -LiteralPath $isolado -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "$falhas falha(s)"
exit ([int]($falhas -gt 0))
