# Helper dos .ps1 do backup externo (issue #1253): envia um script bash para a
# VPS em base64, porque o PowerShell 5 tira as aspas de argumentos do ssh.
# Uso (dot-source): . "$PSScriptRoot\_remoto.ps1"; Invoke-Remoto 'enviar.sh'

$ErrorActionPreference = 'Stop'

function ConvertTo-B64Lf([string]$Caminho) {
    # Normaliza CRLF -> LF: um checkout no Windows nao pode quebrar o bash.
    $texto = [IO.File]::ReadAllText($Caminho) -replace "`r`n", "`n"
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($texto))
}

function Invoke-Remoto {
    param(
        [Parameter(Mandatory)][string]$Script,
        [string]$Prefixo = '',
        [string]$VpsHost = $(if ($env:VPS_HOST) { $env:VPS_HOST } else { 'hostinger' }),
        [switch]$SoMostrar
    )
    $corpo = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $Script)) -replace "`r`n", "`n"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Prefixo + $corpo))
    $remoto = "echo $b64 | base64 -d | bash"
    if ($SoMostrar) {
        Write-Host "ssh $VpsHost `"echo <$($b64.Length) chars base64 de $Script> | base64 -d | bash`""
        return
    }
    & ssh -o BatchMode=yes $VpsHost $remoto
    if ($LASTEXITCODE -ne 0) { throw "$Script terminou com exit $LASTEXITCODE na VPS" }
}
