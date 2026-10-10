param(
    [string]$ApiUrl = 'http://127.0.0.1:18540',
    [Parameter(Mandatory)][string]$ExpectedSha,
    [string]$Email = 'onda0@easystock.local',
    [Parameter(Mandatory)][string]$Senha
)
$ErrorActionPreference = 'Stop'
$uriTeste = [Uri]$ApiUrl
if (-not $uriTeste.IsLoopback -or $uriTeste.Scheme -notin @('http', 'https')) {
    throw 'Este roteiro altera dados e só aceita API local descartável.'
}
if ($ExpectedSha -notmatch '^[a-f0-9]{40}$') { throw 'Informe o SHA completo testado.' }
$ApiUrl = $ApiUrl.TrimEnd('/')
$headersTeste = @{}
function Api([string]$Metodo, [string]$Caminho, $Corpo = $null) {
    $argsHttp = @{ Method=$Metodo; Uri="$ApiUrl$Caminho"; Headers=$headersTeste; TimeoutSec=30 }
    if ($null -ne $Corpo) {
        $argsHttp.ContentType = 'application/json; charset=utf-8'
        $argsHttp.Body = [Text.Encoding]::UTF8.GetBytes(($Corpo | ConvertTo-Json -Depth 8 -Compress))
    }
    (Invoke-RestMethod @argsHttp).data
}
function Conferir($Condicao, [string]$Mensagem) {
    if (-not $Condicao) { throw $Mensagem }
    Write-Host "OK: $Mensagem"
}
$versao = Invoke-RestMethod "$ApiUrl/health/version"
Conferir ($versao.buildSha -eq $ExpectedSha) 'API corresponde ao SHA esperado'
$sessao = Api POST '/api/auth/login' @{ email=$Email; senha=$Senha }
$headersTeste.Authorization = 'Bearer ' + $sessao.token
$lojas = @(Api GET '/api/lojas')
Conferir ($lojas.Count -eq 1 -and $lojas[0].nome -eq 'Cozinha de Teste') 'Loja exclusiva de homologação'
$empresaId = $lojas[0].empresaId
$lojaId = $lojas[0].id
Conferir (@(Api GET '/api/pedidos').Count -eq 0) 'Banco sem pedidos anteriores'
$caixa = Api GET '/api/caixa/dia'
Conferir (-not $caixa.aberto -and -not $caixa.fechado) 'Caixa ainda não movimentado'
$null = Api POST '/api/atendimento/expediente/controle' @{ controle='ForcarAberta' }
$null = Api POST '/api/caixa/abrir' @{ empresaId=$empresaId; saldoInicial=100; observacoes='Homologacao onda 0' }
Conferir ((Api GET '/api/caixa/dia').aberto) 'Caixa aberto pela API'

$pedido = Api POST '/api/pedidos' @{
    empresaId=$empresaId; lojaId=$lojaId; clienteNomeAdHoc='Cliente Teste Onda 0';
    itens=@(@{ nome='Nhoque avulso teste'; quantidade=2.5; precoUnitario=10; observacao='Sem molho' })
}
$pedidoId = $pedido.id
Conferir ($null -ne $pedidoId -and $pedido.total -eq 25) 'Pedido manual fracionado totaliza R$ 25'
$fila = @(Api GET '/api/kds/pedidos')
$card = $fila | Where-Object id -eq $pedidoId
Conferir ($null -ne $card -and @($card.itens).Count -eq 1) 'Item avulso aparece na cozinha'
$null = Api POST "/api/pedidos/$pedidoId/pagamentos" @{
    empresaId=$empresaId; pedidoId=$pedidoId; metodo='dinheiro'; valor=25; referencia='onda0-http'
}
foreach ($status in @('preparando', 'pronto', 'entregue')) {
    $null = Api PATCH "/api/kds/pedidos/$pedidoId/status" @{ status=$status }
    Conferir ((Api GET "/api/pedidos/$pedidoId").pedido.status -eq $status) "Pedido persistido como $status"
}
$caixa = Api GET '/api/caixa/dia'
Conferir ($caixa.totalPagamentosPedidos -eq 25 -and $caixa.saldoEsperado -eq 125) 'Caixa confere R$ 100 de abertura e R$ 25 do pedido'

$cancelar = Api POST '/api/pedidos' @{
    empresaId=$empresaId; lojaId=$lojaId; clienteNomeAdHoc='Cliente Cancelamento Onda 0';
    itens=@(@{ nome='Item cancelado teste'; quantidade=1; precoUnitario=15 })
}
$null = Api POST "/api/pedidos/$($cancelar.id)/pagamentos" @{
    empresaId=$empresaId; pedidoId=$cancelar.id; metodo='dinheiro'; valor=15
}
$null = Api POST "/api/pedidos/$($cancelar.id)/cancelar" @{
    empresaId=$empresaId; id=$cancelar.id; motivo='Cancelamento de teste local'
}
Conferir ((Api GET "/api/pedidos/$($cancelar.id)").pedido.status -eq 'cancelado') 'Cancelamento persistido'
Conferir ((Api GET '/api/caixa/dia').saldoEsperado -eq 140) 'Cancelamento preserva recebimento ainda não devolvido'
$null = Api POST '/api/caixa/fechar' @{ empresaId=$empresaId; observacoes='Onda 0 conferida' }
Conferir ((Api GET '/api/caixa/dia').fechado) 'Caixa fechado e relido'
$null = Api POST '/api/atendimento/expediente/controle' @{ controle='ForcarFechada'; justificativa='Encerramento da homologacao' }
[pscustomobject]@{ sha=$ExpectedSha; pedidoEntregue=$pedidoId; pedidoCancelado=$cancelar.id; saldoConferido=140; homologacao='HTTP local, sem provedores externos nem impressao fisica' } | ConvertTo-Json
