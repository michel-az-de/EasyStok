param(
    [string]$ApiUrl = 'http://127.0.0.1:18540',
    [Parameter(Mandatory)][string]$Senha,
    [Parameter(Mandatory)][string]$ExpectedSha
)
$ErrorActionPreference = 'Stop'
if (-not ([Uri]$ApiUrl).IsLoopback) { throw 'Este roteiro só aceita a API local de homologação.' }
$api = $ApiUrl.TrimEnd('/')
$checks = 0
function Conferir($condicao, $descricao) {
    if (-not $condicao) { throw $descricao }
    $script:checks++
    Write-Output "OK: $descricao"
}
function Request($metodo, $rota, $token, $corpo = $null) {
    $argsHttp = @{ Uri="$api$rota"; Method=$metodo; SkipHttpErrorCheck=$true; TimeoutSec=20 }
    if ($token) { $argsHttp.Headers=@{Authorization="Bearer $token"} }
    if ($null -ne $corpo) { $argsHttp.ContentType='application/json'; $argsHttp.Body=$corpo | ConvertTo-Json -Depth 8 }
    Invoke-WebRequest @argsHttp
}
Conferir ((Invoke-RestMethod "$api/health/version").buildSha -eq $ExpectedSha) 'Build local conferido'
Conferir ((Request GET '/api/auth/me/modulos' $null).StatusCode -eq 401) 'Sem login recebe 401'
$esperados = @{
    Dona=@('cardapio','producao','atendimento','cozinha','financeiro','campanhas','configuracoes','entregas')
    Atendimento=@('cardapio','atendimento','cozinha','financeiro','entregas')
    Cozinha=@('producao','cozinha')
}
foreach($nome in @('Dona','Atendimento','Cozinha')) {
    $login = Request POST '/api/auth/login' $null @{email=$nome.ToLowerInvariant()+'@perfis.local';senha=$Senha}
    Conferir ($login.StatusCode -eq 200) "$nome faz login real"
    $sessao=($login.Content | ConvertFrom-Json).data
    $acesso=((Request GET '/api/auth/me/modulos' $sessao.token).Content | ConvertFrom-Json).data
    $liberados=@($acesso.modulos | Where-Object liberado | ForEach-Object id)
    Conferir (($liberados -join ',') -eq ($esperados[$nome] -join ',')) "$nome recebe somente seus módulos"
    $porta=if($nome -eq 'Dona'){$null}else{$nome.ToLowerInvariant()}
    Conferir ($acesso.portaDeEntrada -eq $porta) "$nome recebe sua porta de entrada"
    foreach($acao in @('editarCardapio','editarProducao','gerenciarCaixa')) {
        Conferir ($acesso.acoes.$acao -eq ($nome -eq 'Dona')) "$nome recebe a capacidade $acao correta"
    }
    foreach($caso in @(
        @('/api/kds/pedidos',200), @('/api/impressao/pendentes',200),
        @('/api/atendimento/comanda/cardapio',200),
        @('/api/atendimento/comanda/cardapio/gestao',$(if($nome -eq 'Cozinha'){403}else{200})),
        @('/api/atendimento/comanda/cardapio/secoes',$(if($nome -eq 'Cozinha'){403}else{200})),
        @('/api/caixa/dia',$(if($nome -eq 'Cozinha'){403}else{200})),
        @('/api/atendimento/conversas',$(if($nome -eq 'Cozinha'){403}else{200})),
        @('/api/atendimento/producao/estoque-do-dia',$(if($nome -eq 'Atendimento'){403}else{200})),
        @('/api/atendimento/producao/insumos',$(if($nome -eq 'Atendimento'){403}else{200})),
        @('/api/minha-vitrine/configuracao',$(if($nome -eq 'Dona'){200}else{403}))
    )) {
        $r=Request GET $caso[0] $sessao.token
        Conferir ($r.StatusCode -eq $caso[1]) "$nome $($caso[0]) retorna $($caso[1]) (recebeu $($r.StatusCode))"
    }
    if($nome -ne 'Dona') {
        Conferir ((Request POST '/api/caixa/fechar' $sessao.token @{}).StatusCode -eq 403) "$nome não fecha o caixa"
        Conferir ((Request POST '/api/caixa/movimentos/00000000-0000-0000-0000-000000000001/estornar' $sessao.token @{}).StatusCode -eq 403) "$nome não estorna lançamento"
        Conferir ((Request POST '/api/atendimento/producao/insumos' $sessao.token @{nome='Nao deve gravar'}).StatusCode -eq 403) "$nome não cadastra insumo"
        Conferir ((Request PUT '/api/atendimento/producao/insumos/00000000-0000-0000-0000-000000000001' $sessao.token @{}).StatusCode -eq 403) "$nome não edita insumo"
        Conferir ((Request PUT '/api/atendimento/producao/receitas/00000000-0000-0000-0000-000000000001/baixa-automatica' $sessao.token @{ligada=$true}).StatusCode -eq 403) "$nome não altera baixa automática"
        Conferir ((Request PUT '/api/produtos/00000000-0000-0000-0000-000000000001/composicao' $sessao.token @{}).StatusCode -eq 403) "$nome não edita receita"
        Conferir ((Request POST '/api/atendimento/comanda/cardapio/secoes' $sessao.token @{nome='Nao deve gravar'}).StatusCode -eq 403) "$nome não edita categorias"
    }
    $refresh=Request POST '/api/auth/refresh' $null @{refreshToken=$sessao.refreshToken}
    Conferir ($refresh.StatusCode -eq 200) "$nome renova a sessão"
    $renovado=($refresh.Content | ConvertFrom-Json).data
    $tokenRenovado=$renovado.accessToken
    $apos=((Request GET '/api/auth/me/modulos' $tokenRenovado).Content | ConvertFrom-Json).data
    Conferir (($apos | ConvertTo-Json -Compress -Depth 5) -eq ($acesso | ConvertTo-Json -Compress -Depth 5)) "$nome mantém módulos e entrada após refresh"
}
Write-Output "Perfis HTTP: $checks verificações aprovadas."
