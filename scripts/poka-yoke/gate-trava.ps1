<#
gate-trava -- trava global da maquina para build e teste do gate (issue 2817).

Por que existe:
  Em 16/11/2026 varias sessoes rodaram o gate ao mesmo tempo; cada uma sobe MSBuild,
  compilador e testhost, a RAM esgotou e a estacao congelou. Com a trava, um gate por vez
  builda e testa na maquina toda; os outros esperam na fila e dizem quem esta na frente.

Mecanica:
  Mutex nomeado do Windows (Global\EasyStokGate). Se o dono morrer sem soltar (gate
  abortado, janela fechada), o Windows marca a trava como abandonada e o proximo da fila
  a recebe: nao existe arquivo de trava para limpar a mao.

Desligar (rollback):  $env:EASYSTOK_GATE_TRAVA = '0'
Teto da espera:       $env:EASYSTOK_GATE_TRAVA_MIN (minutos, padrao 60); estourou = gate falha.
Teste:                powershell -File scripts/poka-yoke/gate-trava.test.ps1

Uso (dot-source):
  . (Join-Path $PSScriptRoot 'gate-trava.ps1')
  $trava = Enter-GateTrava -Info $repoRoot
  try { ... } finally { Exit-GateTrava $trava }
#>
function Enter-GateTrava {
    param([string]$Nome = 'Global\EasyStokGate',
          [double]$EsperaMin = $(if ($env:EASYSTOK_GATE_TRAVA_MIN) { [double]$env:EASYSTOK_GATE_TRAVA_MIN } else { 60 }),
          [string]$Info = '')
    if ($env:EASYSTOK_GATE_TRAVA -eq '0') {
        Write-Host '[gate] trava global DESLIGADA (EASYSTOK_GATE_TRAVA=0).' -ForegroundColor Yellow
        return $null
    }
    $mutex  = New-Object System.Threading.Mutex($false, $Nome)
    $dono   = Join-Path $env:TEMP (($Nome -replace '[\\/:]', '_') + '.dono.txt')
    $relogio = [Diagnostics.Stopwatch]::StartNew()
    $limite = [TimeSpan]::FromMinutes($EsperaMin)
    $obtida = $false; $proximoAviso = 0
    while (-not $obtida) {
        try { $obtida = $mutex.WaitOne(1000) } catch [System.Threading.AbandonedMutexException] { $obtida = $true }
        if ($obtida) { break }
        if ($relogio.Elapsed.TotalSeconds -ge $proximoAviso) {   # na primeira volta e depois a cada minuto
            $quem = if (Test-Path $dono) { (Get-Content $dono -Raw).Trim() } else { 'outro processo' }
            Write-Host ("[gate] aguardando a trava global ha {0:N0} s; em uso por {1}" -f $relogio.Elapsed.TotalSeconds, $quem) -ForegroundColor Yellow
            $proximoAviso += 60
        }
        if ($relogio.Elapsed -ge $limite) {
            $mutex.Dispose()
            throw "[gate] trava global ocupada ha mais de $EsperaMin min. Rode de novo, ou EASYSTOK_GATE_TRAVA=0 para pular a fila."
        }
    }
    Set-Content -Path $dono -Value ("PID {0}, {1}, desde {2:HH:mm:ss}" -f $PID, $Info, (Get-Date)) -ErrorAction SilentlyContinue
    Write-Host ("[gate] trava global obtida apos {0:N1} s de espera." -f $relogio.Elapsed.TotalSeconds)
    [pscustomobject]@{ Mutex = $mutex; Dono = $dono }
}

function Exit-GateTrava($Trava) {
    if (-not $Trava) { return }
    Remove-Item $Trava.Dono -ErrorAction SilentlyContinue   # antes de soltar: o proximo dono grava o dele
    $Trava.Mutex.ReleaseMutex()
    $Trava.Mutex.Dispose()
}
