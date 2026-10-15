# Laboratorio da jornada do pedido sem Docker (#1716): PostgreSQL 17 instalado na maquina, API e Worker deste
# checkout e Mercado Pago falso com pagina de pagamento clicavel. Nada sai para provedor real.
#
#   powershell -File scripts/homologacao/laboratorio.ps1            sobe tudo e prepara a loja
#   powershell -File scripts/homologacao/laboratorio.ps1 -Parar     derruba o que este script subiu
#
# Depois de subir, abra em dois terminais (ou pelo preview do Claude Code):
#   console:  set VITE_FONTE_DADOS=api& set API_ALVO=http://127.0.0.1:18716& npm --prefix EasyStock.Console run dev -- --port 5731
#   site:     set VITE_USE_MOCKS=false& set VITE_API_BASE_URL=& set VITE_DEV_PROXY_TARGET=http://127.0.0.1:18716& npm --prefix ..\casa-da-baba run dev -- --port 5732
# Login do console: usuarios de demonstracao em EasyStock.Api/Data/Tenants/CasaDaBabaSeed.cs.
# O link de comanda sai com o dominio publico: troque por http://localhost:5732/comanda.html?c=TOKEN.
# Compativel com Windows PowerShell 5.1. Texto em ASCII de proposito.
param(
    [switch]$Parar,
    [int]$PortaApi = 18716,
    [int]$PortaMp = 18736,
    [int]$PortaPg = 55716,
    [string]$Pasta = (Join-Path $env:LOCALAPPDATA 'easystok-laboratorio'),
    [string]$PgBin = 'C:\Program Files\PostgreSQL\17\bin'
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pids = Join-Path $Pasta 'pids.txt'
# Senhas de desenvolvimento ja versionadas em docker-compose.local.yml e no seed de demonstracao; so valem em loopback.
$senhaPg = 'EzLocalDev2026'
$api = "http://127.0.0.1:$PortaApi"

function Parar-Laboratorio {
    if (Test-Path $pids) {
        foreach ($id in Get-Content $pids) {
            # PID pode ter sido reaproveitado depois de um reinicio: so encerra o que ainda e python ou dotnet.
            $alvo = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
            if ($alvo -and $alvo.ProcessName -in 'python', 'dotnet') { Stop-Process -Id $alvo.Id -Force -ErrorAction SilentlyContinue }
        }
        Remove-Item $pids
    }
    if (Test-Path (Join-Path $Pasta 'pgdata\postmaster.pid')) {
        & (Join-Path $PgBin 'pg_ctl.exe') -D (Join-Path $Pasta 'pgdata') stop -m fast | Out-Null
    }
    Write-Host 'Laboratorio parado.'
}
if ($Parar) { Parar-Laboratorio; exit 0 }

if (-not (Test-Path (Join-Path $PgBin 'initdb.exe'))) { throw "PostgreSQL 17 nao encontrado em $PgBin. Informe -PgBin." }
foreach ($porta in $PortaApi, $PortaMp) {
    if (Get-NetTCPConnection -LocalPort $porta -State Listen -ErrorAction SilentlyContinue) { throw "Porta $porta em uso. Rode com -Parar ou escolha outra." }
}
New-Item -ItemType Directory -Force $Pasta | Out-Null

# 1. PostgreSQL descartavel, so em loopback.
$dados = Join-Path $Pasta 'pgdata'
if (-not (Test-Path $dados)) {
    $arquivoSenha = Join-Path $Pasta 'pw.txt'
    [IO.File]::WriteAllText($arquivoSenha, $senhaPg)
    & (Join-Path $PgBin 'initdb.exe') -D $dados -U postgres --pwfile=$arquivoSenha -A scram-sha-256 -E UTF8 --locale=C | Out-Null
    Remove-Item $arquivoSenha
}
if (Get-NetTCPConnection -LocalPort $PortaPg -State Listen -ErrorAction SilentlyContinue) {
    # Porta ocupada so vale se for o PostgreSQL deste laboratorio; o de outra sessao nao e nosso para usar.
    if (-not (Test-Path (Join-Path $dados 'postmaster.pid'))) { throw "Porta $PortaPg em uso por outro servidor. Informe -PortaPg." }
} else {
    Start-Process -FilePath (Join-Path $PgBin 'pg_ctl.exe') -WindowStyle Hidden -ArgumentList @(
        '-D', "`"$dados`"", '-l', "`"$(Join-Path $Pasta 'pg.log')`"", '-o', "`"-p $PortaPg -c listen_addresses=127.0.0.1`"", 'start')
    Start-Sleep -Seconds 5
}
$env:PGPASSWORD = $senhaPg
$psql = Join-Path $PgBin 'psql.exe'
$existe = & $psql -h 127.0.0.1 -p $PortaPg -U postgres -tAc "select 1 from pg_database where datname='easystock'"
if ($existe -ne '1') { & $psql -h 127.0.0.1 -p $PortaPg -U postgres -tAc 'create database easystock' | Out-Null }

# 2. Build Release da API e do Worker deste checkout.
Write-Host 'Compilando API e Worker (Release)...'
foreach ($projeto in 'EasyStock.Api', 'EasyStock.Worker') {
    dotnet build (Join-Path $raiz "$projeto\$projeto.csproj") -c Release -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build de $projeto falhou." }
}

# 3. Mercado Pago falso: o cliente real da API fala com ele por HTTP, com webhook assinado.
$iniciados = @()
$env:MP_PORTA = "$PortaMp"; $env:MP_WEBHOOK = "$api/api/webhooks/mercadopago"
$env:MP_URL_PUBLICA = "http://127.0.0.1:$PortaMp"; $env:MP_LOG = Join-Path $Pasta 'mp-chamadas.jsonl'
$iniciados += (Start-Process python -PassThru -WindowStyle Hidden -ArgumentList "`"$(Join-Path $PSScriptRoot 'fake_mp.py')`"").Id

# 4. API (Development) e Worker (Staging: em Development a validacao de DI do Worker impede a subida).
$env:Database__Provider = 'PostgreSql'
$env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$PortaPg;Database=easystock;Username=postgres;Password=$senhaPg;SSL Mode=Disable"
$env:ConnectionStrings__Redis = ''
$env:Jwt__Issuer = 'EasyStock'; $env:Jwt__Audience = 'EasyStock'; $env:Jwt__SecretKey = 'EasyStock-Local-Dev-SuperSecretKey-32+'; $env:Jwt__ExpirationMinutes = '480'
$env:Anthropic__Enabled = 'false'; $env:OpenAI__Enabled = 'false'; $env:FileStorage__Provider = 'Local'; $env:SEED_DEMO_DATA = 'true'
$env:MercadoPago__UseStub = 'false'; $env:MercadoPago__AccessToken = 'token-mp-falso'
$env:MercadoPago__BaseUrl = "http://127.0.0.1:$PortaMp/"; $env:MercadoPago__WebhookSecret = 'homol-mp-secret'
$env:ENABLE_VIACEP_LOOKUP = 'false'; $env:ENABLE_NOMINATIM_GEOCODING = 'false'
$env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = $api
$iniciados += (Start-Process dotnet -PassThru -WindowStyle Hidden -WorkingDirectory (Join-Path $raiz 'EasyStock.Api') `
    -ArgumentList 'bin/Release/net10.0/EasyStock.Api.dll' -RedirectStandardOutput (Join-Path $Pasta 'api.out') -RedirectStandardError (Join-Path $Pasta 'api.err')).Id
$env:ASPNETCORE_ENVIRONMENT = 'Staging'; $env:DOTNET_ENVIRONMENT = 'Staging'; $env:ASPNETCORE_URLS = ''
$iniciados += (Start-Process dotnet -PassThru -WindowStyle Hidden -WorkingDirectory (Join-Path $raiz 'EasyStock.Worker') `
    -ArgumentList 'bin/Release/net10.0/EasyStock.Worker.dll' -RedirectStandardOutput (Join-Path $Pasta 'worker.out') -RedirectStandardError (Join-Path $Pasta 'worker.err')).Id
Set-Content $pids $iniciados

$no_ar = $false
foreach ($i in 1..40) {
    Start-Sleep -Seconds 3
    try { Invoke-RestMethod "$api/health/version" -TimeoutSec 3 | Out-Null; $no_ar = $true; break } catch { }
}
if (-not $no_ar) { throw "A API nao respondeu. Veja $(Join-Path $Pasta 'api.err')." }

# 5. Loja pronta para pedir: vitrine, cardapio, janelas, zona de frete e chat do site.
$login = Invoke-RestMethod -Method Post "$api/api/auth/login" -ContentType 'application/json' -Body '{"email":"admin@casadababa.demo","senha":"admin123"}'
$arquivoToken = Join-Path $Pasta 'token.txt'
[IO.File]::WriteAllText($arquivoToken, $login.data.token)
$cab = @{ Authorization = "Bearer $($login.data.token)" }
# Consulta com identificador entre aspas vai por arquivo: o Windows PowerShell 5.1 come as aspas duplas de um
# argumento de executavel, o psql recebia Slug sem aspas, falhava, e a loja ficava sem preparar (#1761).
function Sql([string]$consulta) {
    $arquivo = Join-Path $Pasta 'consulta.sql'
    [IO.File]::WriteAllText($arquivo, $consulta)
    $saida = & $psql -h 127.0.0.1 -p $PortaPg -U postgres -d easystock -v ON_ERROR_STOP=1 -tAf $arquivo
    $codigo = $LASTEXITCODE
    Remove-Item $arquivo
    if ($codigo -ne 0) { throw "Consulta ao banco do laboratorio falhou: $consulta" }
    return $saida
}
$vitrines = Sql 'select count(*) from storefront where "Slug"=''casa-da-baba'''
if ($vitrines -eq '0') {
    Invoke-RestMethod -Method Post "$api/api/minha-vitrine" -Headers $cab -ContentType 'application/json' -Body '{"tituloPublico":"Casa da Baba","slug":"casa-da-baba"}' | Out-Null
    Invoke-RestMethod -Method Post "$api/api/minha-vitrine/configuracao/ativar" -Headers $cab -ContentType 'application/json' -Body '{}' | Out-Null
    foreach ($carga in 'scripts\cardapio\carregar_cardapio.py', 'scripts\entrega\configurar_janelas.py') {
        python (Join-Path $raiz $carga) --api $api --token-arquivo $arquivoToken --aplicar | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0) { throw "$carga falhou." }
    }
    Invoke-RestMethod -Method Post "$api/api/minha-vitrine/entrega/zonas" -Headers $cab -ContentType 'application/json' `
        -Body '{"label":"Pinheiros e arredores","valor":12,"tempoEstimadoMinutos":60,"ordem":1,"cepInicio":"05400000","cepFim":"05499999","bairros":[]}' | Out-Null
} else { Write-Host 'Loja ja preparada em execucao anterior.' }
foreach ($flag in 'modulo.atendimento', 'atendimento.canal.chatsite') {
    Sql ('insert into "TenantFeatureFlags" ("Id","EmpresaId","Feature","Ativo","AlteradoEm","AlteradoPor") ' +
        "select gen_random_uuid(), e.""Id"", '$flag', true, now(), 'laboratorio' from empresas e " +
        "where not exists (select 1 from ""TenantFeatureFlags"" x where x.""EmpresaId""=e.""Id"" and x.""Feature""='$flag')") | Out-Null
}

Write-Host ''
Write-Host "API            $api"
Write-Host "Mercado Pago   http://127.0.0.1:$PortaMp/_estado   (falso; o link de pagamento abre a pagina de simulacao)"
Write-Host "Banco          127.0.0.1:$PortaPg / easystock"
Write-Host "Logs           $Pasta"
Write-Host 'Frete de teste: CEP 05400-000 a 05499-999 = R$ 12; fora disso cai em entrega a combinar.'
Write-Host 'Para parar:    powershell -File scripts/homologacao/laboratorio.ps1 -Parar'
