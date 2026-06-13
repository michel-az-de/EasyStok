<#
deploy-verify -- confirma que o HEAD do master esta NO AR na VM Azure (Classe F, ADR-0029).

Sem depender de HTTP (TLS bare-IP nao fecha do Windows local). A prova confiavel e o
GIT_SHA do container == HEAD, lido via `az vm run-command invoke` (roda como root na VM).
Ver memoria vm-deploy-verify-recipe / azure-vm-sandbox-docker-topology.

Apos #572 os 3 containers (web/api/admin) carimbam GIT_SHA no env; por isso o default
verifica os tres. A API e o container que roda a logica de negocio (use cases, repos),
entao e o que mais importa confirmar num fix de backend.

Uso:   powershell -File scripts/poka-yoke/deploy-verify.ps1
       powershell -File scripts/poka-yoke/deploy-verify.ps1 -Container easystok-api
       powershell -File scripts/poka-yoke/deploy-verify.ps1 -Containers easystok-web,easystok-api
Saida: exit 0 = todos os containers verificados == HEAD; 1 = algum divergente/ausente
       (deploy pendente — ate o rebuild com #572, api/admin podem nao ter GIT_SHA); 2 = erro/inacessivel.
Requer: az autenticado na subscription que contem easystok-vm (RG EASYSTOK-APP_GROUP).

Gotchas embutidos: bash inline multilinha pelo PS mangla -> escrevo o script remoto num temp
com LF e passo "@file" (token unico). run-command e EXCLUSIVO por VM (1 por vez): se outra
sessao/deploy estiver rodando, vem (Conflict); aguardar e re-tentar.
#>
param(
    [string]$Sha,
    [string[]]$Containers  = @('easystok-web', 'easystok-api', 'easystok-admin'),
    [string]$Container,     # compat: se setado, sobrescreve $Containers com um so
    [string]$ResourceGroup = 'EASYSTOK-APP_GROUP',
    [string]$Vm            = 'easystok-vm',
    [string]$Repo
)
$ErrorActionPreference = 'Stop'
if ($Container) { $Containers = @($Container) }
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
if (-not $Sha)  { $Sha  = (git -C $Repo rev-parse HEAD).Trim() }

# Script remoto (Linux/bash). Escrito em temp com LF e passado como @file (o inline multilinha
# pelo PowerShell embaralha newlines em args). Uma linha 'CONTAINER=<nome>' marca cada bloco;
# os nomes sao interpolados pelo PS (sem variaveis bash -> sem escape de '$').
$blocks = foreach ($c in $Containers) {
@"
echo 'CONTAINER=$c'
docker inspect $c --format '{{range .Config.Env}}{{println .}}{{end}}' 2>/dev/null | grep -i '^GIT_SHA=' || echo 'GIT_SHA=<ausente>'
docker ps --filter 'name=$c' --format 'STATUS={{.Status}}' || echo 'STATUS=<parado>'
"@
}
$remote = ($blocks -join "`n") + @"

echo '=== vm-deploy.log (ultimas linhas) ==='
tail -n 2 /home/azureuser/vm-deploy.log 2>/dev/null || echo '(sem vm-deploy.log)'
"@
$remote = $remote -replace "`r", ""
$tmp = Join-Path $env:TEMP 'deploy-verify-remote.sh'
[IO.File]::WriteAllText($tmp, $remote)

Write-Host "[deploy-verify] HEAD local: $Sha"
Write-Host "[deploy-verify] containers: $($Containers -join ', ')"
Write-Host "[deploy-verify] consultando $Vm ($ResourceGroup) via az vm run-command (exclusivo por VM)..."
$out = az vm run-command invoke -g $ResourceGroup -n $Vm --command-id RunShellScript --scripts "@$tmp" --query "value[0].message" -o tsv
if ($LASTEXITCODE -ne 0 -or -not $out) {
    Write-Host "[deploy-verify] ERRO no az run-command: subscription/tenant errado, VM desligada, ou run-command ocupado (Conflict). Saida:" -ForegroundColor Red
    Write-Host $out
    exit 2
}
Write-Host $out

# Parse: associa cada bloco CONTAINER=<nome> ao GIT_SHA que o segue.
$found = [ordered]@{}
$current = $null
foreach ($line in ($out -split "`n")) {
    if ($line -match '^CONTAINER=(\S+)') { $current = $Matches[1]; $found[$current] = $null; continue }
    if ($current -and $line -match 'GIT_SHA=([0-9a-fA-F]{7,40})') { $found[$current] = $Matches[1] }
}

$allOk = $true
foreach ($c in $Containers) {
    $cSha = $found[$c]
    if (-not $cSha) {
        Write-Host "[deploy-verify] VERMELHO: $c sem GIT_SHA (ausente ou container parado). Deploy pendente." -ForegroundColor Red
        $allOk = $false
        continue
    }
    if ($Sha.StartsWith($cSha) -or $cSha.StartsWith($Sha)) {
        Write-Host "[deploy-verify] VERDE: $c GIT_SHA=$cSha == HEAD." -ForegroundColor Green
    }
    else {
        Write-Host "[deploy-verify] VERMELHO: $c GIT_SHA=$cSha != HEAD=$Sha. Deploy pendente." -ForegroundColor Red
        $allOk = $false
    }
}

if ($allOk) {
    Write-Host "[deploy-verify] OK: todos os containers no ar em HEAD ($Sha)." -ForegroundColor Green
    exit 0
}
else {
    Write-Host "[deploy-verify] Algum container nao reflete o HEAD. Rode scripts/deploy/azure-deploy.sh ou aguarde o cron */5." -ForegroundColor Red
    exit 1
}
