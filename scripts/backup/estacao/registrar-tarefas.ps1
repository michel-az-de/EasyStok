# Prepara a estacao local do backup (E3, #2977, ADR-0068): instala o restic 0.19.1 conferindo
# o SHA256 e registra as tarefas agendadas do usuario. Nao toca na senha nem no rclone.conf.
#   EasyStok-Backup-Puxar      diaria 06:30   puxar.ps1 (copy, retencao, prune guardado, check)
#   EasyStok-Backup-Vigiar     diaria 08:00   vigiar.ps1 (batimento: snapshot <= 26 h)
#   EasyStok-Backup-Restaurar  dia 1, 09:00   restaurar.ps1 (4 itens da S3.5)
# Uso: pwsh scripts/backup/estacao/registrar-tarefas.ps1
. "$PSScriptRoot\comum.ps1"
$cfg = Get-EzbkConfig
$versao = '0.19.1'
$sha = 'da948ad707ed690426473aaba2046cd61f8f90f6f0e7dab6be0d5796531de67d'  # restic_0.19.1_windows_amd64.zip

$atual = if (Test-Path $cfg.Restic) { & $cfg.Restic version } else { '' }
if ($atual -notmatch "restic $([regex]::Escape($versao)) ") {
    $zip = Join-Path ([IO.Path]::GetTempPath()) "restic-$versao.zip"
    Invoke-WebRequest "https://github.com/restic/restic/releases/download/v$versao/restic_${versao}_windows_amd64.zip" -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $sha) { Remove-Item $zip; throw 'SHA256 do restic nao confere' }
    $dir = Split-Path $cfg.Restic
    New-Item -ItemType Directory -Force $dir | Out-Null
    Expand-Archive $zip -DestinationPath $dir -Force
    Move-Item -Force (Join-Path $dir "restic_${versao}_windows_amd64.exe") $cfg.Restic
    Remove-Item $zip
}
& $cfg.Restic version
if (-not $cfg.Rclone) { throw 'rclone ausente no PATH (winget install Rclone.Rclone)' }

$pwsh = (Get-Command pwsh).Source
$tarefas = @(
    @{ Nome = 'EasyStok-Backup-Puxar'; Script = 'puxar.ps1'; Gatilho = New-ScheduledTaskTrigger -Daily -At '06:30' },
    @{ Nome = 'EasyStok-Backup-Vigiar'; Script = 'vigiar.ps1'; Gatilho = New-ScheduledTaskTrigger -Daily -At '08:00' },
    @{ Nome = 'EasyStok-Backup-Restaurar'; Script = 'restaurar.ps1'; Gatilho = $null })
$opcoes = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 4) -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries
foreach ($t in $tarefas) {
    $acao = New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -NonInteractive -File `"$(Join-Path $PSScriptRoot $t.Script)`""
    if ($t.Gatilho) {
        Register-ScheduledTask -TaskName $t.Nome -Action $acao -Trigger $t.Gatilho -Settings $opcoes -Force | Out-Null
    } else {
        # Mensal (dia 1): o cmdlet nao tem gatilho mensal; schtasks tem.
        schtasks /Create /F /TN $t.Nome /SC MONTHLY /D 1 /ST 09:00 /TR "`"$pwsh`" -NoProfile -NonInteractive -File `"$(Join-Path $PSScriptRoot $t.Script)`"" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "schtasks $($t.Nome) falhou" }
    }
    "tarefa $($t.Nome) registrada"
}
if (-not (Test-Path $cfg.SenhaArquivo)) { Write-Warning 'falta a senha: rode estacao\guardar-senha.ps1' }
if (-not (Test-Path $cfg.RcloneConf)) { Write-Warning "falta $($cfg.RcloneConf): docs/dev/backup-drive.md" }
if (Get-ScheduledTask -TaskName PuxarBackupsVPS -ErrorAction SilentlyContinue) {
    Write-Warning 'a tarefa antiga PuxarBackupsVPS segue ativa; o corte e o passo "Corte" do runbook'
}
