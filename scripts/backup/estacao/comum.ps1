# Funcoes comuns da estacao local do backup restic (E3, #2977, ADR-0068).
# Dot-source: . "$PSScriptRoot/comum.ps1". Nada aqui imprime segredo.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

function Get-EzbkConfig {
    $dados = if ($env:EZBK_DADOS) { $env:EZBK_DADOS } else { Join-Path $env:LOCALAPPDATA 'easystok-backup' }
    $rclone = if ($env:EZBK_RCLONE) { $env:EZBK_RCLONE } else { (Get-Command rclone -ErrorAction SilentlyContinue)?.Source }
    [pscustomobject]@{
        Dados        = $dados
        Restic       = if ($env:EZBK_RESTIC) { $env:EZBK_RESTIC } else { Join-Path $dados 'bin/restic.exe' }
        Rclone       = $rclone
        RcloneConf   = Join-Path $dados 'rclone.conf'
        SenhaArquivo = Join-Path $dados 'restic.senha.dpapi'
        RepoRemoto   = 'rclone:ezdrive:easystok-restic'
        RepoLocal    = if ($env:EZBK_REPO_LOCAL) { $env:EZBK_REPO_LOCAL } else { 'C:\backups\restic-easystok' }
        Relatorios   = Join-Path $dados 'relatorios'
        VpsHost      = if ($env:VPS_HOST) { $env:VPS_HOST } else { 'hostinger' }
        Repo         = 'michel-az-de/EasyStok'
        StageVps     = '/home/felipe/backup-restic/stage'
    }
}

# Senha do repositorio guardada com DPAPI (so este usuario nesta maquina abre). Vai para o
# ambiente do processo, nunca para a tela nem para arquivo em claro.
function Set-SenhaRepo([object]$Cfg) {
    if (-not (Test-Path $Cfg.SenhaArquivo)) { throw "senha ausente: rode estacao\guardar-senha.ps1" }
    $seguro = Get-Content $Cfg.SenhaArquivo -Raw | ConvertTo-SecureString
    $texto = [Net.NetworkCredential]::new('', $seguro).Password
    $env:RESTIC_PASSWORD = $texto
    $env:RESTIC_FROM_PASSWORD = $texto
    $env:RCLONE_CONFIG = $Cfg.RcloneConf
}

function Invoke-Restic([object]$Cfg, [string[]]$Argumentos) {
    $base = @('-o', "rclone.program=$($Cfg.Rclone)", '--no-progress')
    $saida = & $Cfg.Restic @base @Argumentos 2>&1
    [pscustomobject]@{ Rc = $LASTEXITCODE; Saida = ($saida | Out-String) }
}

function Read-EstadoKv([string]$Texto) {
    $h = @{}
    foreach ($l in ($Texto -split "`n")) { if ($l -match '^([a-z_]+)=(.*)$') { $h[$Matches[1]] = $Matches[2].Trim() } }
    $h
}

function Test-SnapshotRecente([datetime]$Quando, [datetime]$Agora, [int]$MaxHoras = 26) {
    ($Agora.ToUniversalTime() - $Quando.ToUniversalTime()).TotalHours -le $MaxHoras
}

# Guarda do prune remoto: um token roubado da VPS pode esquecer snapshots; o prune da
# estacao so roda se o remoto nao encolheu demais desde a ultima vez.
function Test-GuardaPrune([int]$Atual, [int]$Anterior, [int]$Minimo = 7, [int]$QuedaMax = 3) {
    if ($Atual -lt $Minimo) { return "remoto com $Atual snapshots (< $Minimo)" }
    if ($Anterior -gt 0 -and ($Anterior - $Atual) -gt $QuedaMax) { return "remoto caiu de $Anterior para $Atual snapshots" }
    $null
}

# KEKs do .env restaurado: EZ_CRYPTO_KEK_ID/EZ_CRYPTO_KEK e a anterior. Mapa id -> bytes.
function Get-KeksDoEnv([string]$Caminho) {
    $kv = @{}
    foreach ($l in Get-Content $Caminho) {
        if ($l -match '^\s*(EZ_CRYPTO_KEK(?:_ANTERIOR)?(?:_ID)?)\s*=\s*"?([^"]*)"?\s*$') { $kv[$Matches[1]] = $Matches[2] }
    }
    $mapa = @{}
    foreach ($p in '', '_ANTERIOR') {
        $id = $kv["EZ_CRYPTO_KEK${p}_ID"]; $chave = $kv["EZ_CRYPTO_KEK$p"]
        if ($id -and $chave) { $mapa[$id] = [Convert]::FromBase64String($chave) }
    }
    $mapa
}

# AES-256-GCM como o IntegrationCredentialResolver (IV 12, tag 16). Devolve so sucesso ou
# falha; o texto decifrado e zerado e nunca sai da funcao.
function Test-DecifraCredencial([byte[]]$Chave, [byte[]]$Cifra, [byte[]]$Iv, [byte[]]$Tag) {
    $claro = [byte[]]::new($Cifra.Length)
    try {
        $aes = [Security.Cryptography.AesGcm]::new($Chave, 16)
        try { $aes.Decrypt($Iv, $Cifra, $Tag, $claro); $true } finally { $aes.Dispose() }
    } catch { $false } finally { [Array]::Clear($claro, 0, $claro.Length) }
}

function Get-TipoArquivo([byte[]]$Inicio) {
    $hex = -join ($Inicio[0..([Math]::Min(3, $Inicio.Length - 1))] | ForEach-Object { $_.ToString('X2') })
    switch -Regex ($hex) {
        '^FFD8FF' { 'jpeg' } '^89504E47' { 'png' } '^25504446' { 'pdf' } '^52494646' { 'webp' }
        '^47494638' { 'gif' } '^504B0304' { 'zip' } default { $null }
    }
}

# Uma issue aberta por origem, no repositorio publico: so o resumo, sem dado nem caminho
# sensivel. Falha comenta; sucesso fecha.
function Set-AlertaBackup([object]$Cfg, [string]$Origem, [string[]]$Falhas) {
    $titulo = "[backup] $Origem"
    $aberta = gh issue list -R $Cfg.Repo --state open --label backup --search "in:title `"$titulo`"" --json number,title |
        ConvertFrom-Json | Where-Object title -eq $titulo | Select-Object -First 1
    if ($Falhas.Count -eq 0) {
        if ($aberta) { gh issue close $aberta.number -R $Cfg.Repo -c "Voltou ao normal em $(Get-Date -Format s)." | Out-Null }
        return
    }
    $corpo = "Detectado em $(Get-Date -Format s) pela estacao local (ADR-0068).`n`n" + (($Falhas | ForEach-Object { "- $_" }) -join "`n") +
        "`n`nDetalhe no relatorio local da estacao. Runbook: docs/dev/backup-drive.md."
    if ($aberta) { gh issue comment $aberta.number -R $Cfg.Repo -b $corpo | Out-Null; return }
    gh label create backup -R $Cfg.Repo -c B60205 -d 'Alerta do backup restic (ADR-0068)' 2>$null | Out-Null
    gh issue create -R $Cfg.Repo -t $titulo -b $corpo -l backup -l infra -l priority:p1 -a michel-az-de | Out-Null
}
