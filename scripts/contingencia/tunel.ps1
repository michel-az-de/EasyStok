# Tunel publico para a API local (#2132): quick tunnel do cloudflared, sem conta e sem abrir porta no roteador.
# Serve para receber webhook da Meta e do Mercado Pago no laboratorio quando a VPS esta fora.
#
#   powershell -File scripts/contingencia/tunel.ps1                 abre o tunel para a API do laboratorio
#   powershell -File scripts/contingencia/tunel.ps1 -Porta 5000     outra porta local
#   powershell -File scripts/contingencia/tunel.ps1 -Parar          fecha o tunel que este script abriu
#
# A URL *.trycloudflare.com muda a cada abertura: cole a nova no painel da Meta e no NotificationUrl do MP.
# Instalar uma vez: winget install --id Cloudflare.cloudflared -e
# Compativel com Windows PowerShell 5.1. Texto em ASCII de proposito.
param(
    [switch]$Parar,
    [int]$Porta = 18716,
    [string]$Pasta = (Join-Path $env:LOCALAPPDATA 'easystok-tunel'),
    [string]$Cloudflared = 'C:\Program Files (x86)\cloudflared\cloudflared.exe'
)
$ErrorActionPreference = 'Stop'
$pidArq = Join-Path $Pasta 'pid.txt'

if ($Parar) {
    if (Test-Path $pidArq) {
        # PID pode ter sido reaproveitado depois de um reinicio: so encerra se ainda for o cloudflared.
        $alvo = Get-Process -Id ([int](Get-Content $pidArq)) -ErrorAction SilentlyContinue
        if ($alvo -and $alvo.ProcessName -eq 'cloudflared') { Stop-Process -Id $alvo.Id -Force }
        Remove-Item $pidArq
    }
    Write-Host 'Tunel fechado.'
    exit 0
}

if (-not (Test-Path $Cloudflared)) {
    $cmd = Get-Command cloudflared -ErrorAction SilentlyContinue
    if (-not $cmd) { throw 'cloudflared nao encontrado. Instale com: winget install --id Cloudflare.cloudflared -e' }
    $Cloudflared = $cmd.Source
}
if (Test-Path $pidArq) { throw "Ja existe um tunel deste script (PID $(Get-Content $pidArq)). Rode com -Parar antes." }
try { Invoke-RestMethod "http://127.0.0.1:$Porta/health/version" -TimeoutSec 3 | Out-Null }
catch { throw "Nada responde em http://127.0.0.1:$Porta/health/version. Suba o laboratorio antes." }

New-Item -ItemType Directory -Force $Pasta | Out-Null
$log = Join-Path $Pasta 'cloudflared.log'
if (Test-Path $log) { Remove-Item $log }
$proc = Start-Process $Cloudflared -PassThru -WindowStyle Hidden -RedirectStandardError $log `
    -ArgumentList 'tunnel', '--no-autoupdate', '--url', "http://127.0.0.1:$Porta"
Set-Content $pidArq $proc.Id

# O cloudflared escreve a URL no stderr; a borda leva alguns segundos para aceitar o nome novo.
$url = $null
foreach ($i in 1..30) {
    Start-Sleep -Seconds 2
    if (Test-Path $log) {
        $achado = Select-String -Path $log -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' | Select-Object -First 1
        if ($achado) { $url = $achado.Matches[0].Value; break }
    }
}
if (-not $url) { throw "O cloudflared nao informou a URL. Veja $log." }
$no_ar = $false
foreach ($i in 1..20) {
    Start-Sleep -Seconds 3
    try { Invoke-RestMethod "$url/health/version" -TimeoutSec 5 | Out-Null; $no_ar = $true; break } catch { }
}
if (-not $no_ar) { Write-Host "Aviso: $url ainda nao respondeu /health/version; tente de novo em um minuto." }

Write-Host ''
Write-Host "Tunel          $url  ->  http://127.0.0.1:$Porta"
Write-Host "Meta           $url/api/webhooks/whatsapp   (painel do app > WhatsApp > Configuracao > Webhook)"
Write-Host "Mercado Pago   $url/api/webhooks/mercadopago   (MercadoPago__NotificationUrl da API)"
Write-Host "Log            $log"
Write-Host 'Para fechar:   powershell -File scripts/contingencia/tunel.ps1 -Parar'
