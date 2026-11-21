# Copia puxada do backup (E3, #2977, ADR-0068): traz os snapshots do Drive para o repositorio
# local, que a VPS nao alcanca, e so entao aplica a retencao com prune nos dois lados.
# Login OAuth da estacao (ezdrive.conf), o mesmo login da VPS: risco aceito em backup-drive.md.
# Agendada por registrar-tarefas.ps1 (06:30). Uso: pwsh estacao/puxar.ps1 [-SemPrune]
param([switch]$SemPrune)
. "$PSScriptRoot\comum.ps1"
$cfg = Get-EzbkConfig
New-Item -ItemType Directory -Force $cfg.Relatorios | Out-Null
$estadoArq = Join-Path $cfg.Dados 'estado-puxar.json'
$anterior = if (Test-Path $estadoArq) { Get-Content $estadoArq -Raw | ConvertFrom-Json } else { $null }
$log = Join-Path $cfg.Relatorios "puxar-$(Get-Date -Format yyyy-MM-dd).log"
$estado = [ordered]@{ data = (Get-Date).ToUniversalTime().ToString('s') + 'Z'; ok = $false; motivo = ''; remotos = 0 }

function Passo([string]$Nome, [string]$Repo, [string[]]$A) {
    $r = Invoke-Restic $cfg (@('-r', $Repo) + $A)
    Add-Content $log "== $Nome (rc $($r.Rc))`n$($r.Saida)"
    if ($r.Rc -ne 0) { throw "$Nome saiu com $($r.Rc)" }
    $r
}

try {
    Set-SenhaRepo $cfg
    $c = Invoke-Restic $cfg @('-r', $cfg.RepoLocal, 'cat', 'config')
    if ($c.Rc -eq 10) { Passo 'init local' $cfg.RepoLocal @('init', '--from-repo', $cfg.RepoRemoto, '--copy-chunker-params') | Out-Null }
    elseif ($c.Rc -ne 0) { throw "repositorio local saiu com $($c.Rc)" }

    Passo 'copy' $cfg.RepoLocal @('copy', '--from-repo', $cfg.RepoRemoto) | Out-Null

    $snaps = (Passo 'snapshots remoto' $cfg.RepoRemoto @('snapshots', '--json')).Saida | ConvertFrom-Json
    $estado.remotos = @($snaps).Count
    $guarda = Test-GuardaPrune $estado.remotos ([int]($anterior?.remotos ?? 0))

    # Mesma retencao da VPS (S3.7): os forget sao idempotentes; aqui o prune apaga de fato.
    $politicas = @(
        @('--tag', 'diario', '--keep-daily', '7', '--keep-weekly', '4', '--keep-monthly', '12'),
        @('--tag', 'pre-deploy', '--keep-last', '10'),
        @('--tag', 'rotulos', '--keep-within', '5y'))
    foreach ($p in $politicas) {
        Passo "forget local $($p[1])" $cfg.RepoLocal (@('forget', '--host', 'easystok-vps', '--group-by', 'host,tags', '--prune') + $p) | Out-Null
    }
    Passo 'check local' $cfg.RepoLocal @('check', '--read-data-subset', '10%') | Out-Null

    if ($SemPrune) { $estado.motivo = 'prune remoto pulado (-SemPrune)' }
    elseif ($guarda) { $estado.motivo = "prune remoto bloqueado: $guarda" }
    else {
        Passo 'prune remoto' $cfg.RepoRemoto @('prune') | Out-Null
        Passo 'check remoto' $cfg.RepoRemoto @('check', '--read-data-subset', '2%') | Out-Null
    }
    $estado.ok = -not $guarda
    if ($guarda) { throw $estado.motivo }
} catch {
    $estado.motivo = "$_"
    Add-Content $log "ERRO: $_"
} finally {
    Remove-Item Env:RESTIC_PASSWORD, Env:RESTIC_FROM_PASSWORD -ErrorAction SilentlyContinue
    $estado | ConvertTo-Json | Set-Content $estadoArq
}
if (-not $estado.ok) { Write-Error "puxar falhou: $($estado.motivo)"; exit 1 }
"puxar ok: $($estado.remotos) snapshots no Drive"
