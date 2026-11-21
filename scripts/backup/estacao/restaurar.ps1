# Restauracao mensal de prova (S3.5, E3 #2977, ADR-0068). Restaura o ultimo snapshot diario
# do repositorio LOCAL num postgres:17 descartavel e aprova os 4 itens:
#   1. contagens de todas as tabelas iguais ao manifesto tirado no mesmo snapshot do dump;
#   2. RLS: como easystock, 0 linhas sem empresa e as linhas da empresa com ela;
#   3. KEK: as credenciais do cofre decifram com a KEK do .env restaurado (texto nunca exibido);
#   4. um arquivo de uploads-data abre com assinatura de arquivo valida.
# Falha abre ou comenta a issue "[backup] restauracao mensal"; sucesso fecha. Tudo que foi
# restaurado (inclusive o .env em claro) e apagado no fim.
# Uso: pwsh estacao/restaurar.ps1            (agendado no dia 1 as 09:00)
#      pwsh estacao/restaurar.ps1 -Pasta X   (testes: pula o restic e usa um stage pronto)
param([string]$Pasta, [switch]$SemAlerta, [string]$Imagem = 'postgres:17')
. "$PSScriptRoot/comum.ps1"
$cfg = Get-EzbkConfig
New-Item -ItemType Directory -Force $cfg.Relatorios | Out-Null
$rel = Join-Path $cfg.Relatorios "restauracao-$(Get-Date -Format yyyy-MM-dd).txt"
$itens = [ordered]@{ '1 contagens' = 'nao rodou'; '2 RLS' = 'nao rodou'; '3 KEK' = 'nao rodou'; '4 uploads' = 'nao rodou' }
$tmp = Join-Path ([IO.Path]::GetTempPath()) "ezbk-rest-$PID"
$pg = "ezbk-restauracao-$PID"
$erro = $null

function Sql([string]$Consulta, [string]$Banco = 'easystock') {
    $s = $Consulta | docker exec -i $pg psql -U postgres -d $Banco -X -q -A -t -v ON_ERROR_STOP=1 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql: $($s | Out-String)" }
    # Virgula: sem ela uma linha so volta como string e [0] pega o primeiro caractere.
    , @($s | ForEach-Object { "$_" } | Where-Object { $_ -ne '' })
}

try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    if (-not $Pasta) {
        Set-SenhaRepo $cfg
        $r = Invoke-Restic $cfg @('-r', $cfg.RepoLocal, 'restore', "latest:$($cfg.StageVps)", '--tag', 'diario', '--target', (Join-Path $tmp 'snap'))
        if ($r.Rc -ne 0) { throw "restic restore saiu com $($r.Rc)" }
        $Pasta = Join-Path $tmp 'snap'
    }
    $db = Join-Path $Pasta 'db'
    docker run -d --name $pg --network none -e POSTGRES_PASSWORD=$([guid]::NewGuid()) $Imagem | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker run $Imagem falhou" }
    foreach ($i in 1..60) { docker exec $pg pg_isready -U postgres -q 2>$null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 1 }
    Start-Sleep 2
    docker cp (Join-Path $db 'globals.sql') "${pg}:/tmp/globals.sql" | Out-Null
    docker cp (Join-Path $db 'easystock.dump') "${pg}:/tmp/easystock.dump" | Out-Null
    # globals traz a role easystock (NOBYPASSRLS) e os grants; "role postgres ja existe" e esperado.
    docker exec $pg psql -U postgres -X -q -f /tmp/globals.sql 2>&1 | Out-Null
    Sql 'CREATE DATABASE easystock OWNER easystock' 'postgres' | Out-Null
    $o = docker exec $pg pg_restore -U postgres -d easystock --exit-on-error /tmp/easystock.dump 2>&1
    if ($LASTEXITCODE -ne 0) { throw "pg_restore: $($o | Out-String)" }

    # 1. contagens
    $manif = @(Get-Content (Join-Path $db 'manifesto.txt') | Where-Object { $_ -match '\|' })
    $dif = @(foreach ($l in $manif) {
        $t, $n = $l -split '\|'
        $real = (Sql "SELECT count(*) FROM public.`"$t`"")[0]
        if ($real -ne $n) { "$t (manifesto $n, restaurado $real)" }
    })
    $itens['1 contagens'] = if (-not $manif) { 'FALHA: manifesto vazio' } elseif ($dif) { "FALHA: $($dif -join '; ')" } else { "OK: $($manif.Count) tabelas" }

    # 2. RLS: a tabela com FORCE RLS e mais linhas
    $alvo = Sql @'
SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE n.nspname = 'public' AND c.relrowsecurity AND c.relforcerowsecurity
   AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'EmpresaId')
 ORDER BY c.reltuples DESC, c.relname
'@
    $t = $null
    foreach ($cand in $alvo) { if ((Sql "SELECT count(*) FROM public.`"$cand`"")[0] -ne '0') { $t = $cand; break } }
    if (-not $t) { $itens['2 RLS'] = 'FALHA: nenhuma tabela com FORCE RLS e linhas' }
    else {
        $emp, $total = (Sql "SELECT `"EmpresaId`"::text || '|' || count(*)::text FROM public.`"$t`" GROUP BY `"EmpresaId`" ORDER BY count(*) DESC LIMIT 1")[0] -split '\|'
        $sem = (Sql "SET ROLE easystock; SELECT count(*) FROM public.`"$t`"")[-1]
        $com = (Sql "SET ROLE easystock; SET app.empresa_id = '$emp'; SELECT count(*) FROM public.`"$t`"")[-1]
        $itens['2 RLS'] = if ($sem -eq '0' -and $com -eq $total) { "OK: $t, 0 sem empresa e $com com ela" }
                          else { "FALHA: $t, $sem sem empresa e $com com ela (esperado 0 e $total)" }
    }

    # 3. KEK: as credenciais do cofre decifram com o .env restaurado
    $envArq = Get-ChildItem -Path $Pasta -Recurse -Filter '.env' -Force -File |
        Where-Object { $_.Directory.Name -eq 'easystok' } | Select-Object -First 1
    $creds = Sql "SELECT kek_id || '|' || translate(encode(payload_cifrado,'base64'), E'\n', '') || '|' || encode(iv,'base64') || '|' || encode(tag,'base64') FROM credencial_integracao"
    if (-not $envArq) { $itens['3 KEK'] = 'FALHA: .env ausente no snapshot' }
    elseif (-not $creds) { $itens['3 KEK'] = 'FALHA: cofre vazio, nada a decifrar' }
    else {
        $keks = Get-KeksDoEnv $envArq.FullName
        $ruins = 0
        foreach ($c in $creds) {
            $id, $cifra, $iv, $tag = $c -split '\|'
            $ok = $keks.ContainsKey($id) -and (Test-DecifraCredencial $keks[$id] ([Convert]::FromBase64String($cifra)) ([Convert]::FromBase64String($iv)) ([Convert]::FromBase64String($tag)))
            if (-not $ok) { $ruins++ }
        }
        $itens['3 KEK'] = if ($ruins) { "FALHA: $ruins de $($creds.Count) credenciais nao decifram" } else { "OK: $($creds.Count) credenciais decifram" }
    }

    # 4. uploads: o primeiro arquivo do tar com assinatura conhecida
    $tar = Get-ChildItem -Path (Join-Path $Pasta 'volumes') -Filter '*uploads-data.tar' | Select-Object -First 1
    if (-not $tar) { $itens['4 uploads'] = 'FALHA: tar de uploads-data ausente' }
    else {
        $lista = @(tar -tf $tar.FullName | Where-Object { $_ -notmatch '/$' } | Select-Object -First 50)
        $dest = Join-Path $tmp 'uploads'; New-Item -ItemType Directory -Force $dest | Out-Null
        $achado = $null
        foreach ($f in $lista) {
            tar -xf $tar.FullName -C $dest $f 2>$null
            $p = Join-Path $dest $f
            if (Test-Path -LiteralPath $p -PathType Leaf) {
                $b = [byte[]]@([IO.File]::ReadAllBytes($p) | Select-Object -First 4)
                if ($b.Length -ge 4 -and ($tipo = Get-TipoArquivo $b)) { $achado = "$tipo, $((Get-Item -LiteralPath $p).Length) bytes"; break }
            }
        }
        $itens['4 uploads'] = if ($achado) { "OK: $achado" } elseif ($lista) { 'FALHA: nenhum arquivo com assinatura valida' } else { 'FALHA: tar vazio' }
    }
} catch { $erro = "$_" }
finally {
    Remove-Item Env:RESTIC_PASSWORD, Env:RESTIC_FROM_PASSWORD -ErrorAction SilentlyContinue
    docker rm -f $pg 2>$null | Out-Null
    if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
}

$falhas = @($itens.GetEnumerator() | Where-Object { $_.Value -notlike 'OK*' } | ForEach-Object { "$($_.Key): $($_.Value)" })
if ($erro) { $falhas = @("erro: $erro") + $falhas }
$texto = "restauracao $(Get-Date -Format s)`n" + (($itens.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" }) -join "`n") + $(if ($erro) { "`nerro: $erro" } else { '' })
Set-Content $rel $texto
$texto
if (-not $SemAlerta) { Set-AlertaBackup $cfg 'restauracao mensal' $falhas }
if ($falhas.Count) { exit 1 }
