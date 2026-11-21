# Instala o backup restic na VPS (E3, #2977): copia backup.sh, instalar.sh e as units para
# ~/backup-restic/src em LF e roda o instalar.sh la. Nao envia segredo nenhum.
# Uso: pwsh scripts/backup/instalar.ps1 [-VpsHost hostinger] [-SemTimer]
param([string]$VpsHost = $(if ($env:VPS_HOST) { $env:VPS_HOST } else { 'hostinger' }), [switch]$SemTimer)
$ErrorActionPreference = 'Stop'
$arquivos = 'backup.sh', 'instalar.sh', 'systemd/easystok-backup.service', 'systemd/easystok-backup.timer'
& ssh -o BatchMode=yes $VpsHost 'mkdir -p ~/backup-restic/src/systemd && chmod 700 ~/backup-restic'
if ($LASTEXITCODE -ne 0) { throw "ssh $VpsHost falhou" }
foreach ($a in $arquivos) {
    # CRLF -> LF: um checkout no Windows nao pode quebrar o bash.
    $texto = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $a)) -replace "`r`n", "`n"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($texto))
    $b64 | & ssh -o BatchMode=yes $VpsHost "base64 -d > ~/backup-restic/src/$a"
    if ($LASTEXITCODE -ne 0) { throw "copia de $a falhou" }
}
$flag = if ($SemTimer) { ' --sem-timer' } else { '' }
& ssh -o BatchMode=yes $VpsHost "bash ~/backup-restic/src/instalar.sh$flag"
if ($LASTEXITCODE -ne 0) { throw "instalar.sh terminou com exit $LASTEXITCODE" }
