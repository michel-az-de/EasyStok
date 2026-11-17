<#
gate-trava.test.ps1 -- prova a trava global do gate (issue 2817) com processos reais.

Por que existe:
  Em 16/11/2026 varios gates rodaram ao mesmo tempo, a RAM da estacao esgotou e o PC
  congelou. A trava faz o segundo gate esperar o primeiro terminar. Cada caso sobe
  processos powershell separados (o proprio script no papel de "ator") disputando um
  mutex de nome unico, para nao esbarrar num gate real em andamento.

Uso:   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/poka-yoke/gate-trava.test.ps1
Saida: exit 0 = todos os casos ok; exit 1 = algum caso falhou.
#>
param([string]$Papel, [string]$Nome, [double]$Segura = 0, [string]$Saida,
      [switch]$NaoLibera, [double]$EsperaMin = 1)

# -- Papel de ator: pega a trava, segura, solta e grava os horarios ----------
if ($Papel) {
    $r = @{ papel = $Papel }
    try {
        . (Join-Path $PSScriptRoot 'gate-trava.ps1')
        $t = Enter-GateTrava -Nome $Nome -EsperaMin $EsperaMin -Info "teste $Papel"
        $r.obtida = (Get-Date).ToString('o')
        Set-Content "$Saida.obtida" $r.obtida
        Start-Sleep -Milliseconds ([int]($Segura * 1000))
        $r.liberada = (Get-Date).ToString('o')   # antes de soltar: quem esperava so entra depois
        if (-not $NaoLibera) { Exit-GateTrava $t }
    } catch { $r.erro = $_.Exception.Message }
    $r | ConvertTo-Json | Set-Content $Saida
    exit 0
}

# -- Orquestrador ------------------------------------------------------------
$ErrorActionPreference = 'Stop'
$pasta = Join-Path $env:TEMP ("gate-trava-teste-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $pasta | Out-Null
$falhas = 0

function Ator([string]$papel, [string]$nome, [double]$segura, [double]$espera = 1, [switch]$naoLibera) {
    $saida = Join-Path $pasta "$papel.json"
    $arg = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
             '-Papel', $papel, '-Nome', $nome, '-Segura', $segura, '-Saida', "`"$saida`"", '-EsperaMin', $espera)
    if ($naoLibera) { $arg += '-NaoLibera' }
    $p = Start-Process powershell.exe -ArgumentList $arg -PassThru -WindowStyle Hidden
    [pscustomobject]@{ Processo = $p; Saida = $saida }
}
function AguardaObtida($ator) {
    $limite = (Get-Date).AddSeconds(30)
    while (-not (Test-Path "$($ator.Saida).obtida")) {
        if ((Get-Date) -gt $limite -or $ator.Processo.HasExited) { return $false }
        Start-Sleep -Milliseconds 100
    }
    $true
}
function Resultado($ator) {
    if (-not $ator.Processo.WaitForExit(60000)) { $ator.Processo.Kill(); return @{ erro = 'ator nao terminou em 60 s' } }
    if (-not (Test-Path $ator.Saida)) { return @{ erro = 'ator morreu sem gravar resultado' } }
    Get-Content $ator.Saida -Raw | ConvertFrom-Json
}
function Confere([string]$caso, [bool]$ok, [string]$detalhe) {
    if ($ok) { Write-Host "ok    $caso" } else { Write-Host "FALHA $caso -- $detalhe" -ForegroundColor Red; $script:falhas++ }
}
function NovoNome { "Global\EasyStokGateTeste-" + [guid]::NewGuid().ToString('N') }

try {
    # 1. O segundo espera o primeiro soltar.
    $nome = NovoNome
    $a = Ator 'espera-a' $nome 4
    if (AguardaObtida $a) {
        $b = Ator 'espera-b' $nome 0
        $ra = Resultado $a; $rb = Resultado $b
        Confere 'segundo gate espera o primeiro soltar a trava' `
            ($rb.obtida -and $ra.liberada -and [datetime]$rb.obtida -ge [datetime]$ra.liberada) `
            "a: $($ra | ConvertTo-Json -Compress) b: $($rb | ConvertTo-Json -Compress)"
    } else { Confere 'segundo gate espera o primeiro soltar a trava' $false "o primeiro ator nao pegou a trava: $((Resultado $a) | ConvertTo-Json -Compress)" }

    # 2. Com EASYSTOK_GATE_TRAVA=0 ninguem espera (rollback da issue).
    $nome = NovoNome
    $a = Ator 'desligada-a' $nome 4
    if (AguardaObtida $a) {
        $env:EASYSTOK_GATE_TRAVA = '0'
        try { $b = Ator 'desligada-b' $nome 0 } finally { Remove-Item Env:EASYSTOK_GATE_TRAVA }
        $ra = Resultado $a; $rb = Resultado $b
        Confere 'EASYSTOK_GATE_TRAVA=0 desliga a espera' `
            ($rb.obtida -and $ra.liberada -and [datetime]$rb.obtida -lt [datetime]$ra.liberada) `
            "a: $($ra | ConvertTo-Json -Compress) b: $($rb | ConvertTo-Json -Compress)"
    } else { Confere 'EASYSTOK_GATE_TRAVA=0 desliga a espera' $false "o primeiro ator nao pegou a trava: $((Resultado $a) | ConvertTo-Json -Compress)" }

    # 3. Dono morto sem soltar (gate abortado) nao prende a maquina.
    $nome = NovoNome
    $a = Ator 'abandonada-a' $nome 0 -naoLibera
    $ra = Resultado $a
    $b = Ator 'abandonada-b' $nome 0
    $rb = Resultado $b
    Confere 'trava de processo morto passa para o proximo' ([bool]($ra.obtida -and $rb.obtida -and -not $rb.erro)) `
        "a: $($ra | ConvertTo-Json -Compress) b: $($rb | ConvertTo-Json -Compress)"

    # 4. Espera tem teto: estourou, falha com mensagem em vez de travar para sempre.
    $nome = NovoNome
    $a = Ator 'teto-a' $nome 6
    if (AguardaObtida $a) {
        $b = Ator 'teto-b' $nome 0 0.02   # 1,2 s de teto
        $rb = Resultado $b; $null = Resultado $a
        Confere 'espera acima do teto falha com mensagem' ([bool]($rb.erro -match 'ocupada' -and -not $rb.obtida)) `
            "b: $($rb | ConvertTo-Json -Compress)"
    } else { Confere 'espera acima do teto falha com mensagem' $false "o primeiro ator nao pegou a trava: $((Resultado $a) | ConvertTo-Json -Compress)" }
} finally {
    Get-ChildItem $pasta -File | Remove-Item -Force
    Remove-Item $pasta -Force
}

if ($falhas -gt 0) { Write-Host "$falhas caso(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host 'Trava global do gate: todos os casos ok.' -ForegroundColor Green
exit 0
