# Roda um envio do backup externo agora, fora do cron (issue #1253). Uso: .\enviar-agora.ps1 [-SoMostrar]
param([switch]$SoMostrar)
. "$PSScriptRoot\_remoto.ps1"
Invoke-Remoto -Script 'enviar.sh' -SoMostrar:$SoMostrar
