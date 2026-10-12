# Instala o backup externo na VPS (issue #1253). Uso: .\instalar.ps1 [-SoMostrar]
# Pre-requisito: ~/.config/easystok-backup/rclone.conf na VPS (docs/dev/backup-externo.md, passo A).
param([switch]$SoMostrar)
. "$PSScriptRoot\_remoto.ps1"
$prefixo = "export ENVIAR_B64=$(ConvertTo-B64Lf (Join-Path $PSScriptRoot 'enviar.sh'))`n" +
           "export TESTAR_B64=$(ConvertTo-B64Lf (Join-Path $PSScriptRoot 'testar-restauracao.sh'))`n"
Invoke-Remoto -Script 'instalar.sh' -Prefixo $prefixo -SoMostrar:$SoMostrar
