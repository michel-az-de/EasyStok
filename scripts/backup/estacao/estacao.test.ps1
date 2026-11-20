# Testes da estacao do backup (E3, #2977). Unidade das funcoes de comum.ps1 e, ponta a ponta,
# backup.sh (dump, config, volumes) -> restaurar.ps1 num postgres:17 descartavel com RLS,
# cofre cifrado com uma KEK de teste e um PNG no volume de uploads. Precisa de docker e bash.
# Uso: pwsh scripts/backup/estacao/estacao.test.ps1
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/comum.ps1"
$script:falhas = 0
function Confere([string]$Nome, [bool]$Ok, [string]$Detalhe = '') {
    if ($Ok) { "ok   $Nome" } else { "FAIL $Nome $Detalhe"; $script:falhas++ }
}

# ---------------------------------------------------------------- unidade
Confere 'guarda: remoto saudavel libera prune' ($null -eq (Test-GuardaPrune 20 21))
Confere 'guarda: poucos snapshots bloqueia'   ($null -ne (Test-GuardaPrune 3 0))
Confere 'guarda: queda brusca bloqueia'       ($null -ne (Test-GuardaPrune 10 20))
$agora = [datetime]'2026-11-20T12:00:00Z'
Confere 'recente: 25 h passa'                 (Test-SnapshotRecente $agora.AddHours(-25) $agora)
Confere 'recente: 27 h falha'                 (-not (Test-SnapshotRecente $agora.AddHours(-27) $agora))
$e = Read-EstadoKv "data=x`nmodo=diario`nrc=4`nmotivo=pg_dump falhou`n"
Confere 'estado: le rc e motivo'              ($e['rc'] -eq '4' -and $e['motivo'] -eq 'pg_dump falhou')
Confere 'tipo: png'                           ((Get-TipoArquivo ([byte[]](0x89, 0x50, 0x4E, 0x47))) -eq 'png')
Confere 'tipo: texto nao passa'               ($null -eq (Get-TipoArquivo ([byte[]](0x61, 0x62, 0x63, 0x64))))

$kek = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$kekVelha = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
function Cifra([byte[]]$Chave, [string]$Texto) {
    $claro = [Text.Encoding]::UTF8.GetBytes($Texto)
    $iv = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    $c = [byte[]]::new($claro.Length); $t = [byte[]]::new(16)
    $aes = [Security.Cryptography.AesGcm]::new($Chave, 16); $aes.Encrypt($iv, $claro, $c, $t); $aes.Dispose()
    @{ c = $c; iv = $iv; t = $t }
}
$x = Cifra $kek '{"token":"teste"}'
Confere 'aes: decifra com a KEK certa'        (Test-DecifraCredencial $kek $x.c $x.iv $x.t)
Confere 'aes: KEK errada falha'               (-not (Test-DecifraCredencial $kekVelha $x.c $x.iv $x.t))

# ---------------------------------------------------------------- ponta a ponta
$bash = if ($IsWindows) { Join-Path $env:ProgramFiles 'Git\bin\bash.exe' } else { 'bash' }
$w = Join-Path ([IO.Path]::GetTempPath()) "ezbk-test-$PID"
$pg = "ezbk-test-pg-$PID"; $vol = "ezbk-test-$PID-uploads-data"
$stack = Join-Path $w 'stacks/easystok'
New-Item -ItemType Directory -Force $stack, (Join-Path $w 'shared/initdb'), (Join-Path $w 'dados') | Out-Null
$env:EZBK_DADOS = Join-Path $w 'dados'
try {
    docker run -d --name $pg -e POSTGRES_PASSWORD=teste postgres:17 | Out-Null
    docker volume create $vol | Out-Null
    docker run --rm -v "${vol}:/d" alpine:latest sh -c 'mkdir -p /d/fotos && printf "\x89PNG\r\n\x1a\nresto" > /d/fotos/p.png' | Out-Null
    foreach ($i in 1..60) { docker exec $pg pg_isready -U postgres -q 2>$null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 1 }
    Start-Sleep 2
    $emp = '00000000-0000-0000-0000-0000000000aa'
    $b64 = { param($b) [Convert]::ToBase64String($b) }
    $y = Cifra $kekVelha '{"token":"antigo"}'
    $sql = @"
CREATE ROLE easystock LOGIN PASSWORD 'nao-vaza' NOBYPASSRLS;
CREATE DATABASE easystock OWNER easystock;
\c easystock
SET ROLE easystock;
CREATE TABLE "Produtos" ("Id" int, "EmpresaId" uuid);
INSERT INTO "Produtos" SELECT g, '$emp' FROM generate_series(1, 5) g;
INSERT INTO "Produtos" SELECT g, '00000000-0000-0000-0000-0000000000bb' FROM generate_series(1, 2) g;
ALTER TABLE "Produtos" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Produtos" FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON "Produtos"
  USING (current_setting('app.bypass_rls', true) = 'true' OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
CREATE TABLE credencial_integracao (kek_id text, payload_cifrado bytea, iv bytea, tag bytea);
INSERT INTO credencial_integracao VALUES
  ('kek-atual', decode('$(& $b64 $x.c)','base64'), decode('$(& $b64 $x.iv)','base64'), decode('$(& $b64 $x.t)','base64')),
  ('kek-velha', decode('$(& $b64 $y.c)','base64'), decode('$(& $b64 $y.iv)','base64'), decode('$(& $b64 $y.t)','base64'));
"@
    $sql | docker exec -i $pg psql -U postgres -q -v ON_ERROR_STOP=1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'schema de teste falhou' }
    Set-Content (Join-Path $stack '.env') "EZ_CRYPTO_KEK_ID=kek-atual`nEZ_CRYPTO_KEK=$(& $b64 $kek)`nEZ_CRYPTO_KEK_ANTERIOR_ID=kek-velha`nEZ_CRYPTO_KEK_ANTERIOR=$(& $b64 $kekVelha)"
    Set-Content (Join-Path $stack 'compose.yaml') 'services: {}'
    Set-Content (Join-Path $w 'shared/initdb/30-easystok.sql') '-- teste'

    # Stage gerado pelas funcoes reais do backup.sh
    $u = { param($p) $p -replace '\\', '/' }
    $env:MSYS_NO_PATHCONV = '1'
    $env:BACKUP_SO_FUNCOES = '1'; $env:BASE = (& $u $w) + '/b'; $env:DB_CONTAINER = $pg
    $env:STACK_DIR = & $u $stack; $env:SHARED_DIR = (& $u $w) + '/shared'; $env:VOLUMES = $vol
    & $bash -c "source '$(& $u (Join-Path $PSScriptRoot '../backup.sh'))' && mkdir -p `"`$STAGE`" && dump_banco && coleta_config && coleta_volumes"
    Confere 'backup.sh gerou o stage' ($LASTEXITCODE -eq 0)
    $stage = Join-Path $w 'b/stage'

    $out = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'restaurar.ps1') -Pasta $stage -SemAlerta 2>&1 | Out-String
    $rc = $LASTEXITCODE
    Confere 'restauracao: exit 0'           ($rc -eq 0) $out
    Confere 'restauracao: 1 contagens'      ($out -match '1 contagens: OK: 2 tabelas') $out
    Confere 'restauracao: 2 RLS'            ($out -match '2 RLS: OK: Produtos, 0 sem empresa e 5 com ela') $out
    Confere 'restauracao: 3 KEK atual e anterior' ($out -match '3 KEK: OK: 2 credenciais') $out
    Confere 'restauracao: 4 uploads'        ($out -match '4 uploads: OK: png') $out
    Confere 'restauracao: nao imprime o cofre' ($out -notmatch 'token')
    Confere 'restauracao: container removido' (-not (docker ps -aq --filter "name=ezbk-restauracao"))

    # Negativos: KEK trocada e manifesto adulterado
    $envStage = Get-ChildItem $stage -Recurse -Filter '.env' -Force -File | Select-Object -First 1
    (Get-Content $envStage.FullName) -replace '^EZ_CRYPTO_KEK=.*', "EZ_CRYPTO_KEK=$(& $b64 $kekVelha)" | Set-Content $envStage.FullName
    $m = Join-Path $stage 'db/manifesto.txt'
    (Get-Content $m) -replace '^Produtos\|7$', 'Produtos|8' | Set-Content $m
    $out = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'restaurar.ps1') -Pasta $stage -SemAlerta 2>&1 | Out-String
    Confere 'negativo: exit 1'              ($LASTEXITCODE -eq 1) $out
    Confere 'negativo: KEK errada reprova'  ($out -match '3 KEK: FALHA: 1 de 2') $out
    Confere 'negativo: contagem reprova'    ($out -match '1 contagens: FALHA: Produtos \(manifesto 8, restaurado 7\)') $out
} finally {
    docker rm -f $pg 2>$null | Out-Null
    docker volume rm -f $vol 2>$null | Out-Null
    'BACKUP_SO_FUNCOES', 'BASE', 'DB_CONTAINER', 'STACK_DIR', 'SHARED_DIR', 'VOLUMES', 'EZBK_DADOS', 'MSYS_NO_PATHCONV' |
        ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $w -Recurse -Force -ErrorAction SilentlyContinue
}
if ($script:falhas) { "$($script:falhas) FALHA(S)"; exit 1 }
'TODOS OS CASOS PASSARAM'
