# Baixa o backup externo mais recente e restaura num postgres descartavel na VPS (issue #1253).
# Uso: .\testar-restauracao.ps1 [-SoMostrar]
param([switch]$SoMostrar)
. "$PSScriptRoot\_remoto.ps1"
Invoke-Remoto -Script 'testar-restauracao.sh' -SoMostrar:$SoMostrar
