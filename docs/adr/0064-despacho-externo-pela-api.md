# ADR-0064 · Despacho externo pela API: corrida com status do provedor, saída sem entregador cadastrado

- Status: Proposto (2026-10-31; registra as decisões do Felipe de 31/10)
- Data: 2026-10-31
- Decisor: Felipe
- Emenda: ADR-0061 D5, na parte "flag desligada em produção" (ver D6).
- Relacionados: #2158 (parte 1), #1247 (F17), épico #1633, plano de logística #1597
  (`origin/docs/plano-logistica-entregas-1597`, regras R1 a R12), ADR-0061 (D3 webhook, D5 custo),
  ADR-0051 (viagem), ADR-0010 (RLS)

## Contexto

Hoje a corrida de terceiro só é anotada à mão (`CorridaExterna.RegistrarManual`: empresa, custo, motorista).
A Lalamove já é chamada de verdade, mas só para precificar o frete do checkout (`CotadorFreteLalamove`).
O cliente HTTP assinado já sabe criar, consultar e cancelar pedido, e ninguém o usa fora do simulador.

Decisões do Felipe em 31/10: só a Lalamove agora (a 99 fica manual até a API ser confirmada por escrito,
ADR-0061 D5), descongelar o despacho no console (#1889 congelou em 20/10) e o operador dispara a corrida
com um clique. Depois disso o status vem sozinho.

Fatos que pesam no desenho:

| # | Fato | Fonte |
|---|---|---|
| 1 | Viagem não sai sem `Entregador` cadastrado (RN-32) | `Viagem.Sair` |
| 2 | Já existe o retorno da entrega: o pedido volta de `SaiuParaEntrega` para `Pronto` | `Pedido.RegistrarRetornoEntrega`, `Viagem.RegistrarRetorno` |
| 3 | Já existe a deduplicação de webhook por provedor e id do evento, guardando só o hash do corpo | `WebhookRecebido` (`ux_webhook_recebidos_provedor_eventid`) |
| 4 | `POST /v3/orders` da Lalamove não tem idempotência; o `Request-ID` é só nonce de suporte | Plano #1597, seção 3 |
| 5 | O webhook da Lalamove chega fora de ordem, tenta 10 vezes em 24 h e depois desativa a URL | Plano #1597, seção 3 |
| 6 | O suporte da Lalamove pode cancelar e abrir outro pedido no lugar (`ORDER_REPLACED` com `prevOrderId`) | Plano #1597, seção 3 |
| 7 | Cancelamento sem custo só em `ASSIGNING_DRIVER` ou até 5 minutos depois do match | Doc da Lalamove |

## Decisão

### D1 · A corrida da API é a mesma `CorridaExterna`, com status

`CorridaExterna` ganha `Origem = Api` e as colunas do provedor (status, veículo, cotação, custo cotado, id
externo e anterior, link de rastreio, motorista com id, telefone e placa, instante do match e do
encerramento). Na corrida manual essas colunas ficam nulas e nada muda nela. O custo continua num lugar só
(ADR-0061 D5): nasce com o cotado e é trocado pelo preço final do provedor.

`StatusCorrida`: `Solicitando`, `ProcurandoMotorista`, `MotoristaACaminho`, `Coletada`, `Entregue`,
`Cancelada`, `Rejeitada`, `Expirada`, `Falha`. O provedor é a fonte da verdade. O domínio aceita pular
etapa, porque o webhook chega fora de ordem e sempre se reconsulta, mas estado final não reabre. A
exceção é a troca de pedido pelo suporte, que mantém a corrida e troca o id.

Corrida da API não aceita a correção manual de custo e motorista: esses dados vêm do provedor.

### D2 · Saída da viagem sem entregador cadastrado (R1)

`Viagem.SairComCorrida(corrida)` põe a viagem em rota quando o provedor confirma a coleta, sem
`EntregadorId`. O retrato de cada parada vem do motorista do provedor (nome, veículo, placa, empresa).
A RN-32 continua valendo para o entregador próprio.

### D3 · Não entregue reaproveita o retorno que já existe (R2)

Corrida encerrada sem entrega (`Cancelada`, `Rejeitada`, `Expirada`) antes da coleta deixa a viagem
montando, para chamar de novo ou passar ao entregador próprio. Depois da coleta, usa o retorno que já
existe (`Viagem.RegistrarRetorno` e `Pedido.RegistrarRetornoEntrega`). `PedidoStateMachine` não muda.

### D4 · Uma corrida em curso por viagem, gravada antes do POST (R4)

A corrida nasce `Solicitando` e é gravada antes do POST, com o próprio id no `metadata` do pedido. Um
índice único parcial (`ux_corridas_externas_viagem_ativa`, status 1 a 4) barra a segunda em curso na
mesma viagem, venha de dois cliques ou de dois hosts. O POST não é repetido: num timeout, a reconsulta
reconcilia. O id do provedor é único por provedor (`ux_corridas_externas_provedor_id_externo`).

### D5 · Webhook e dado pessoal (R5, R12)

A entrada segue a ADR-0061 D3: chave opaca da loja no caminho da URL, assinatura com a chave e o segredo
da loja, deduplicação em `WebhookRecebido` e reconsulta ao provedor antes de mudar estado. O corpo do
webhook não é gravado, só o hash. Nome e telefone do motorista ficam só na corrida, e ao provedor vai só
nome, telefone e endereço da entrega.

### D6 · A conexão da loja é a chave de liga e desliga

A ADR-0061 D5 previa uma flag desligada em produção. Em 31/10 o Felipe decidiu descongelar já. O botão
"Chamar Lalamove" só aparece com a conexão Lalamove ativa na loja, e o ambiente da conexão (sandbox ou
produção) decide o host. Desligar é remover ou desativar a conexão. O gate de produção do plano #1597
vira gate operacional: a primeira corrida real é acompanhada antes do uso diário.

## Consequências

- Migração aditiva em `corridas_externas`: colunas nulas e dois índices parciais. Nenhum dado existente muda.
- A 99 e as outras empresas continuam pela corrida manual. Um adapter novo reaproveita D1 a D5.
- Multiparada fica fora: uma parada por corrida (R7), porque o `COMPLETED` da Lalamove é um só para N paradas.
- Rollback por camada: desligar a conexão, cancelar as corridas ativas no painel da Lalamove, tirar a URL
  do webhook do Partner Portal e só então reverter o código. A migração fica.
