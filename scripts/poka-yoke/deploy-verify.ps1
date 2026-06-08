<#
deploy-verify -- confirma que o HEAD do master esta NO AR na VM Azure (Classe F, ADR-0029).

Sem depender de HTTP (TLS bare-IP nao fecha do Windows local). A prova confiavel e o
GIT_SHA do container == HEAD, lido via `az vm run-command invoke` (roda como root na VM).
Ver memoria vm-deploy-verify-recipe / azure-vm-sandbox-docker-topology.

Uso:   powershell -File scripts/poka-yoke/deploy-verify.ps1 [-Sha <sha>] [-Container easystok-web]
Saida: exit 0 = container GIT_SHA == HEAD (no ar); 1 = divergente (deploy pendente); 2 = erro/inacessivel.
Requer: az autenticado na subscription que contem easystok-vm (RG EASYSTOK-APP_GROUP).

Gotchas embutidos: bash inline multilinha pelo PS mangla -> escrevo o script remoto num temp
com LF e passo "@file" (token unico). run-command e EXCLUSIVO por VM (1 por vez): se outra
sessao/deploy estiver rodando, vem (Conflict); aguardar e re-tentar.
#>
param(
    [string]$Sha,
    [string]$Container     = 'easystok-web',
    [string]$ResourceGroup = 'EASYSTOK-APP_GROUP',
    [string]$Vm            = 'easystok-vm',
    [string]$Repo
)
$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
if (-not $Sha)  { $Sha  = (git -C $Repo rev-parse HEAD).Trim() }

# Script remoto (Linux/bash). Escrito em temp com LF e passado como @file (o inline multilinha
# pelo PowerShell embaralha newlines em args).
$remote = @"
echo '=== GIT_SHA do container ==='
docker inspect $Container --format '{{range .Config.Env}}{{println .}}{{end}}' 2>/dev/null | grep -i GIT_SHA || echo 'GIT_SHA nao encontrado'
echo '=== status do container ==='
docker ps --filter "name=$Container" --format '{{.Names}} {{.Status}}'
echo '=== ultimas linhas do vm-deploy.log ==='
tail -n 2 /home/azureuser/vm-deploy.log 2>/dev/null || echo '(sem vm-deploy.log)'
"@
$remote = $remote -replace "`r", ""
$tmp = Join-Path $env:TEMP 'deploy-verify-remote.sh'
[IO.File]::WriteAllText($tmp, $remote)

Write-Host "[deploy-verify] HEAD local: $Sha"
Write-Host "[deploy-verify] consultando $Vm ($ResourceGroup) via az vm run-command (exclusivo por VM)..."
$out = az vm run-command invoke -g $ResourceGroup -n $Vm --command-id RunShellScript --scripts "@$tmp" --query "value[0].message" -o tsv
if ($LASTEXITCODE -ne 0 -or -not $out) {
    Write-Host "[deploy-verify] ERRO no az run-command: subscription/tenant errado, VM desligada, ou run-command ocupado (Conflict). Saida:" -ForegroundColor Red
    Write-Host $out
    exit 2
}
Write-Host $out
$m = [regex]::Match($out, 'GIT_SHA=([0-9a-fA-F]{7,40})')
if (-not $m.Success) { Write-Host "[deploy-verify] nao consegui ler GIT_SHA do container." -ForegroundColor Red; exit 2 }
$containerSha = $m.Groups[1].Value

if ($Sha.StartsWith($containerSha) -or $containerSha.StartsWith($Sha)) {
    Write-Host "[deploy-verify] VERDE: container GIT_SHA=$containerSha == HEAD. O HEAD esta no ar." -ForegroundColor Green
    exit 0
}
else {
    Write-Host "[deploy-verify] VERMELHO: container GIT_SHA=$containerSha != HEAD=$Sha. Deploy pendente (rode scripts/deploy/azure-deploy.sh ou aguarde o cron */5)." -ForegroundColor Red
    exit 1
}
