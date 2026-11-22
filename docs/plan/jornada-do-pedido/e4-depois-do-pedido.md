# E4: Receber, ver a entrega e enxergar o que entra (#3076, onda 2)

- Data: 2026-11-21
- Base medida: `origin/master` `5c7e2bdb3`, mais a onda 1 da #3076 (PR #3102, correções do Console). Referências conferidas de novo em `ed2013b77` (inclui a #2813, quitado do histórico).
- Issue: #3076. Relacionadas: #2944 (plano da jornada), E2 (`e2-retirada-endereco-cliente-nova.md`), ADR-0044.
- Status: proposta. Tier alto (feat sem spec aprovada, contrato de API novo, adendo em ADR). PR aberta até a label `aprovado` do Felipe.
- Origem: medição ao vivo de 21/11 com API local semeada, roteiro de 4 tarefas do dia a dia.

## 1. Problema

A medição da linha de base deu:

| Tarefa | Antes | Situação |
|---|---|---|
| T1 Cadastrar cliente | 5 cliques + 3 digitações | OK |
| T2 Registrar pedido | 14 cliques + 4 digitações | OK; a onda 1 levou a 6 cliques |
| T3 Receber pedido | sem caminho em Pedidos | FALHA |
| T4 Lançar conta paga | 9 cliques + 3 digitações | OK |

A onda 1 corrigiu os bugs. Ficaram três lacunas que pedem comportamento novo, e por isso vêm aqui:

| Lacuna | Onde | Evidência |
|---|---|---|
| L1 Detalhe do pedido só leitura: sem "Registrar pagamento", sem endereço e sem forma de pagamento no topo | `features/gestao/cadastros/DetalhePedido.jsx` | T3 impossível em Pedidos; hoje só pela conversa |
| L2 Lista de Pedidos sem a data da entrega | `features/gestao/cadastros/PainelPedidos.jsx` | quem separa a produção do dia abre pedido por pedido |
| L3 "A receber nos próximos 30 dias" da Visão geral ignora pedido sem conta a receber | `features/gestao/financeiro/PainelFinanceiro.jsx:87` (`totalAVencer30dReceber`, que soma só parcelas: `FluxoCaixaQueries.cs:42-44`) | pedido de R$ 132,30 sem pagamento e sem conta não aparece no card |

## 2. Proposta

### L1. Registrar pagamento no detalhe

O detalhe ganha cabeçalho com entrega (`agendadoParaEm`, endereço ou retirada) e pagamento (forma, pago ou saldo). Ganha também o botão "Registrar pagamento" quando o pedido tem saldo a receber.

O saldo e o botão leem `aReceber` do `PedidoResult` (`PedidoResult.cs:30`, #2813), que é zero no pedido quitado pelo import do histórico. A tela não refaz total menos pago. Sem `aReceber` (API antiga), cai em `situacaoDoPagamento(pedido).falta` (`dominio/pedidosCadastro.js:148-158`). O pedido quitado do histórico mostra que está quitado e não mostra o botão: a API recusa a baixa nele (`RegistrarPagamentoPedidoUseCase.cs:82`).

`aReceber` vem de `AReceberPelosPagamentos` (`Pedido.cs:452`), que não olha o status nem o cupom. A API calcula o pendente pelo valor já alocado, e esse alocado inclui o cupom (`RegistrarPagamentoPedidoUseCase.cs:118-119`, `SaldoFinanceiroPedido.cs:8`). Limite da v1: em pedido com cupom, o valor sugerido pode passar do pendente. Nesse caso a recusa "excede o pendente" da API aparece no detalhe, ao lado de "Abrir conversa", onde a cobrança já lê o saldo com cupom (`dominio/pagamento.js:96-98`).

O botão NÃO ganha caminho novo. Ele reaproveita o `receber` da conversa (`aplicacao/api/comanda.js:271-313`). O caminho `aplicacao/pagamento.js` não existe. O detalhe roda fora do `AtendimentoProvider` (`DetalhePedido.jsx:15-17`), então o corpo do `receber` sai para uma função de `aplicacao/` que recebe o `pedidoId` e o pedido, sem ler a conversa. Conversa e detalhe chamam a mesma função. Ela mantém:

| Regra | Onde hoje |
|---|---|
| Idempotência: uma chave por envio; repetir após queda não lança duas vezes | `comanda.js:56,288`; `Idempotency-Key` em `infra/api/pagamentosPedidoApi.js:9-11`; `POST /api/pedidos/{id}/pagamentos` em `PedidosController.cs:201-203` (`EasyStock.Api/Controllers`) |
| Aprovação: pedido que espera aprovação recusa a baixa ("Aprove o pedido antes de registrar o pagamento.") | `comanda.js:283-286`; `dominio/cobranca.js:430-434` |
| Link do Mercado Pago pendente: a forma vira "na entrega" (cancela o link) e só então registra | `comanda.js:287,289`; `cobranca.js:436-438` |
| `pedido_ja_pago`: mostra a mensagem da API, não o erro genérico | `comanda.js:298`; `CobrancaPedidoConflitoException.cs:10` |
| Sem confirmação da rede: confere o pedido antes de aceitar nova tentativa | `comanda.js:299,302` |

O detalhe só tem o `PedidoResult` (`PedidoResult.cs:4-31`), sem `cobranca` nem `requerAprovacao`. Por isso, em pedido pré-operacional (`rascunho`, `aguardando_pagamento`, `aguardando_aprovacao_baba`; `PedidoStateMachine.cs:112-116`, espelho em `pedidos-cockpit.js:33,143`) e em pedido `cancelado` (a API recusa: `RegistrarPagamentoPedidoUseCase.cs:103-110`) o botão fica desligado, com o motivo, e o detalhe aponta "Abrir conversa" (`DetalhePedido.jsx:26`), onde link e aprovação já se resolvem. Sem regra nova de valor.

### L2. Coluna Entrega, ordem e filtro "Entrega hoje"

Hoje `GET /api/pedidos` (`EasyStock.Api/Controllers/PedidosController.cs`) aceita `page, pageSize, status, clienteId, desde, ate, search, sort, order` (`PedidosController.cs:45-56`). `desde` e `ate` filtram `CriadoEm` (`PedidoRepository.cs:76-77`). `sort` aceita `urgencia`, `total`, `status`, `cliente` e, por padrão, `criadoem` (`PedidoRepository.cs:100-109`). A resposta já traz `agendadoParaEm` (`PedidoResult.cs:24`). O pedido não tem campo de fim de janela (`Pedido.cs` só tem `AgendadoParaEm:136` e `InicioPrevistoEm:206`), então a coluna mostra dia e hora, não faixa.

Contrato novo, tudo opcional e aditivo em `GET /api/pedidos`:

| Parâmetro | Regra |
|---|---|
| `sort=entrega` | Ordena pelo dia da entrega (definição abaixo). Desempate por `Id`, como hoje (`PedidoRepository.cs:111`) |
| `entregaDesde`, `entregaAte` | Dia civil de Brasília, normalizado como `desde` e `ate` (`DataUtc.InicioDoPeriodoUtc` e `FimDoPeriodoUtc`, `PedidosController.cs:62`). Filtram pelo dia da entrega |

**Dia da entrega** = `AgendadoParaEm`; sem ele (pedido para agora), `CriadoEm`. A L2 e a L3 usam a mesma definição.

Mudam: `PedidosController.cs:45-63`, `ListarPedidosQuery` (`ListarPedidosUseCase.cs:6-16`), `IPedidoRepository.cs:19-29` e `PedidoRepository.cs:51-114`. No Console, `ORDENS_PEDIDO` ganha "Entrega" (`pedidosCadastro.js:102-107`) e `consultaDePedidos` envia `entregaDesde` e `entregaAte` iguais a hoje no filtro "Entrega hoje" (`pedidosCadastro.js:111-125`).

### L3. Linha "Pedidos a receber"

**Decidido pelo Felipe em 2026-11-21, 18:27**, na sessão gestora, literal: "Linha própria" (opção B; a A somava pedidos e contas no mesmo número). O número do card "A receber nos próximos 30 dias" segue só com as contas. Embaixo entra "Pedidos a receber: R$ X (M pedidos)".

**Por que precisa de exclusão.** Com a flag `GerarContaReceberAutomaticaDePedido` ligada, o pedido que entra no status `StatusPedidoQueGeraContaReceber` (padrão `entregue`, `ConfiguracaoLoja.cs:33`) ganha uma `ContaReceber` de origem Pedido (`AtualizarStatusPedidoUseCase.cs:219-226`). A conta nasce com o saldo do momento (`GerarContaReceberDePedidoUseCase.cs:65-69`) e vencimento em 7 dias (`:79`). Se a linha somasse esse pedido, o mesmo dinheiro entraria no card e na linha. A linha só conta pedido SEM conta.

**Critério exato.** Entra na linha o pedido que cumpre TODAS:

1. Status fora de `cancelado` e fora do pré-operacional (`rascunho`, `aguardando_pagamento`, `aguardando_aprovacao_baba`). É a regra do cockpit (`pedidos-cockpit.js:92`); o detalhe (`pedidosCadastro.js:148-158`) só exclui o cancelado.
2. Saldo maior que zero, com saldo = `Pedido.AReceberPelosPagamentos` (`Pedido.cs:452`): `Total` menos a soma dos `Pagamentos` (`TotalPago`, `:435`), e zero no pedido quitado pelo import do histórico (`QuitadoHistoricoEm`, `:129`; gravado em `LegacySnapshotImportService.cs:466`). No SQL, `QuitadoHistoricoEm == null`, como `DashboardAnalyticsQueries.cs:294`; a propriedade calculada `QuitadoHistorico` não traduz. Pago, pago a mais, total zero e quitado do histórico ficam fora. Estorno e cupom não entram na v1 (`SaldoFinanceiroPedido.cs:9,34-35` os considera; fica como limite declarado).
3. Sem `ContaReceber` de origem Pedido com `OrigemRefId` igual ao pedido e status diferente de `Cancelada`. Conta cancelada não está no card, então o pedido volta a contar.
4. Dia da entrega (definição da L2) antes de hoje mais 31 dias. `AgendadoParaEm` é um instante UTC: converte-se para o dia civil de Brasília (`HorarioBrasil`) ANTES de comparar com o corte do card (`FluxoCaixaQueries.cs:21`). Sem limite inferior, **decidido pelo Felipe em 2026-11-21, 23:29**, literal: "Sim, continua". Pedido já entregue e não pago segue a receber, mesmo entregue há mais de 30 dias, e pedido não tem "Vencidas". Entregue e não pago conta, como no cockpit.

**Contrato.** Sai pelo `GET /api/financeiro/dashboard` (`FinanceiroController.cs:18-25`). `DashboardFinanceiroDto` ganha dois campos opcionais com padrão, como `QtdContasReceberAVencer30d` (`IFluxoCaixaQueries.cs:33-49`):

- `TotalPedidosAReceber` (`decimal`, padrão `0m`)
- `QtdPedidosAReceber` (`int`, padrão `0`)

O cálculo entra em `FluxoCaixaQueries.KpisDashboardAsync` (`FluxoCaixaQueries.cs:15`), junto das parcelas vivas. Soma e contagem rodam no banco, com a soma dos `Pagamentos` em subconsulta, sem carregar pedidos em memória: sem limite inferior, o conjunto cresce com o histórico. O Console lê os campos em `PainelFinanceiro.jsx:87`, só para quem vê "receber" (`:62`, `:87`), e esconde a linha se o campo não vier (API antiga) ou se `QtdPedidosAReceber` for 0.

**Reconciliação com a ADR-0044** (`docs/adr/0044-definicoes-de-a-receber.md`). A ADR manda nomear honestamente e não unificar antes de resolver três decisões de produto. A E4 respeita:

- Não unifica. O número do card não muda, e nenhum dos quatro números da tabela da ADR muda de valor.
- A linha é a quinta medida, com rótulo próprio ("Pedidos a receber") e `title` que diz: pedidos sem conta a receber e ainda não pagos, com dia da entrega até 30 dias à frente ou em qualquer dia passado; saldo = total menos pagamentos. O rótulo não promete "próximos 30 dias".
- Responde só no escopo da linha à decisão 2 (pedido e parcela são o mesmo dinheiro?): com conta, o dinheiro é o da conta; sem conta, é o do pedido. Isso evita a soma em dobro sem unificar nada. A decisão 1 a linha resolve como o cockpit (entregue e não pago conta). A decisão 3 segue em aberto.
- A ADR cita como satélite que a conta gerada usa `Total` cheio. Desde a #2095 (F10) ela usa o saldo (`GerarContaReceberDePedidoUseCase.cs:65-69`). A PR de implementação põe um adendo datado na ADR: a quinta linha na tabela e a correção desse satélite.
- Fica de fora da linha todo pedido com conta. O pagamento feito no pedido depois da conta gerada não é reconciliado com a parcela. É o satélite que a ADR já guarda, e a linha não o agrava.

## 3. Fora

- E2 (retirada, escolha de endereço, cliente nova): spec própria, já decidida, ninguém começou.
- Regra de celular: o cadastro aceita telefone fixo e celular fora do padrão que o pedido recusa para avisar no WhatsApp. É decisão de produto (exigir celular no cadastro ou só no pedido); hoje o pedido já diz o motivo.
- Cliente segue "Lead" depois do primeiro pedido: depende da regra de relacionamento da API (#2777).
- Caixa com data do dia e pagamento em dinheiro com caixa fechado: comportamento decidido (#2319, #2736), não é bug.
- Unificar os números de "a receber" (épico da ADR-0044).

## 4. Aceite

- [ ] T3 em Pedidos: abrir o pedido (1), "Registrar pagamento" (2), escolher o método (3), confirmar (4); saldo zero. No máximo 4 cliques.
- [ ] L1 usa a função compartilhada com a conversa. Teste prova a mesma chave de idempotência no reenvio, a recusa por aprovação, o cancelamento do link e a mensagem de `pedido_ja_pago`. Pedido pré-operacional e pedido cancelado mostram o botão desligado com o motivo. Em pedido com cupom, a recusa "excede o pendente" aparece no detalhe com "Abrir conversa".
- [ ] Lista de Pedidos com a data da entrega visível em 1440 e 390 px. `sort=entrega` ordena pelo dia da entrega. "Entrega hoje" lista só pedidos com entrega hoje (inclui o pedido para agora criado hoje).
- [ ] L3, exemplo verificável, loja com a flag da conta automática DESLIGADA: conta a receber de R$ 120 vencendo em 10 dias e pedido operacional sem conta, total R$ 132,30, sem pagamento, entrega em 5 dias. O card mostra R$ 120,00 e a linha mostra "Pedidos a receber: R$ 132,30 (1 pedido)".
- [ ] L3, mesma loja com a flag ligada ANTES da entrega e o pedido já entregue: ele ganha a conta de R$ 132,30. O card passa a R$ 252,30 (conta com vencimento em 7 dias, dentro dos 30) e o pedido NÃO aparece na linha, que some por zerar.
- [ ] L3, exclusões: pedido cancelado, pré-operacional, pago, quitado do histórico, com entrega além de 31 dias ou com conta não cancelada (inclusive a paga) não entra na linha. Pagamento parcial entra só com o saldo. Pedido com conta cancelada volta à linha.
- [ ] L3, borda de fuso: pedido agendado às 21h de Brasília em hoje+30 (já é hoje+31 em UTC) conta como hoje+30 e ENTRA na linha; às 21h de hoje+31 NÃO entra.
- [ ] L3, pedido antigo (decisão do Felipe, 2026-11-21): pedido operacional sem conta, entregue há 45 dias, total R$ 80,00, sem pagamento, ENTRA na linha com R$ 80,00; o card das contas não muda.
- [ ] Quitado do histórico (#2813): pedido entregue de R$ 80,00, sem pagamento e sem conta, com `QuitadoHistoricoEm` preenchido pelo import, NÃO entra na linha da L3. No detalhe, aparece como quitado e sem "Registrar pagamento".
- [ ] `GET /api/financeiro/dashboard` sem os campos novos (API antiga) não quebra o Console: a linha não aparece.
- [ ] Prova Red→Green por lacuna e roteiro de navegador com as 4 tarefas, antes e depois.
- [ ] `npm run qualidade` verde; CI verde; `/code-review` sem Critical.

## 5. Tier e Rollback

Tier alto, pela natureza: feat sem spec aprovada, contrato de API novo (`/api/pedidos` e `/api/financeiro/dashboard`, ambos só aditivos e opcionais) e adendo em ADR (processo). PR aberta até a label `aprovado`.

Rollback: reverter a PR. Sem migração: L2 e L3 só consultam, sem coluna nem índice novo. Cliente antigo ignora os campos e parâmetros novos. Console novo contra API antiga esconde a linha da L3 e perde só a ordem e o filtro de entrega. O adendo da ADR volta junto na reversão.
