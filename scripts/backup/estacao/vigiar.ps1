# Vigia diario do backup (E3, #2977, ADR-0068): o batimento de sucesso. Confere o ultimo
# snapshot diario no Drive (no maximo 26 h), o estado do backup.sh na VPS e a ultima puxada.
# Qualquer falha abre ou comenta a issue "[backup] vigia"; tudo ok fecha. Agendado as 08:00.
. "$PSScriptRoot\comum.ps1"
$cfg = Get-EzbkConfig
$agora = Get-Date
$falhas = [Collections.Generic.List[string]]::new()
try {
    Set-SenhaRepo $cfg
    $r = Invoke-Restic $cfg @('-r', $cfg.RepoRemoto, 'snapshots', '--tag', 'diario', '--latest', '1', '--json')
    if ($r.Rc -ne 0) { $falhas.Add("Drive ilegivel (restic saiu com $($r.Rc))") }
    else {
        $ult = @($r.Saida | ConvertFrom-Json) | Sort-Object time | Select-Object -Last 1
        if (-not $ult) { $falhas.Add('nenhum snapshot diario no Drive') }
        elseif (-not (Test-SnapshotRecente ([datetime]$ult.time) $agora)) { $falhas.Add("ultimo snapshot diario de $(([datetime]$ult.time).ToString('s'))") }
    }
} catch { $falhas.Add("vigia sem acesso ao Drive: $($_.Exception.Message)") }
finally { Remove-Item Env:RESTIC_PASSWORD, Env:RESTIC_FROM_PASSWORD -ErrorAction SilentlyContinue }

$txt = & ssh -o BatchMode=yes -o ConnectTimeout=20 $cfg.VpsHost 'cat ~/backup-restic/estado-diario' 2>$null
if ($LASTEXITCODE -ne 0) { $falhas.Add('estado do backup na VPS ilegivel') }
else {
    $e = Read-EstadoKv ($txt | Out-String)
    if ($e['rc'] -ne '0') { $falhas.Add("backup.sh na VPS saiu com $($e['rc']): $($e['motivo'])") }
}

$p = Join-Path $cfg.Dados 'estado-puxar.json'
if (-not (Test-Path $p)) { $falhas.Add('puxada local nunca rodou') }
else {
    $ep = Get-Content $p -Raw | ConvertFrom-Json
    if (-not $ep.ok) { $falhas.Add("puxada local falhou: $($ep.motivo)") }
    elseif (-not (Test-SnapshotRecente ([datetime]$ep.data) $agora)) { $falhas.Add("ultima puxada local em $($ep.data)") }
}

Set-AlertaBackup $cfg 'vigia' $falhas
if ($falhas.Count) { $falhas | ForEach-Object { Write-Warning $_ }; exit 1 }
'vigia ok'
