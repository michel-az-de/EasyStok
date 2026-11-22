# E4: Receber, ver a entrega e enxergar o que entra (#3076, onda 2)

- Data: 2026-11-21
- Base medida: `origin/master` `5c7e2bdb3`, mais a onda 1 da #3076 (PR #3102, correções do Console). Referências conferidas de novo em `ed2013b77` (inclui a #2813, quitado do histórico). As decisões da #3130 (seção 2.4) e as referências que elas tocam foram conferidas em `7c6f5dbaf`.
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

No detalhe (`GET /api/pedidos/{id}`), `aReceber` passa a vir de `SaldoFinanceiroPedido.AReceberAsync(pedido)` (`SaldoFinanceiroPedido.cs:34-37`), pela mesma conta que o KDS faz em lote (`KdsPedidoQueries.cs:125`; o KDS não zera o quitado do histórico). É a mesma conta da API na baixa: o pendente sai do valor alocado, que inclui cupom e estorno (`RegistrarPagamentoPedidoUseCase.cs:118-119`, `SaldoFinanceiroPedido.cs:8`). Usa a sobrecarga que recebe o `Pedido`, que zera o quitado do histórico; a sobrecarga por `Guid` (`:39`) não zera. Entra em `ObterPedidoDetalhesUseCase` como parâmetro opcional `SaldoFinanceiroPedido? saldos = null`, no padrão de `QuitacaoPedido.cs:31`: os testes que constroem o use case só com o repositório seguem valendo. `ExecuteAsync` ganha `CancellationToken ct = default` (ADR-0013), e o `GET` passa o da requisição. Efeitos:

- O valor sugerido é o pendente da API, então pedido com cupom não cai mais na recusa "excede o pendente". A recusa ainda pode vir por corrida (outra tela recebeu no meio) e aparece no detalhe com a mensagem da API.
- Pedido entregue com estorno passa a mostrar o saldo e o botão, pela regra que a entrega e o KDS já usam (`SaldoFinanceiroPedido.cs:46-51`, `KdsPedidoQueries.cs:125`).
- No cancelado, a API passa a devolver total menos alocado. A tela não lê esse valor (parágrafo abaixo).
- A lista (`CriarPedidoUseCase.cs:229`) e a resposta do `POST` de pagamento (`RegistrarPagamentoPedidoUseCase.cs:181`) seguem com `AReceberPelosPagamentos`. Depois da baixa, o próprio `DetalhePedido` relê o pedido por `GET` (`obterPedido` de `aplicacao/pedidosCadastro.js`) e avisa quem o abriu (`aoMudar`), para a lista de Pedidos recarregar; isso vale nos três lugares que abrem o detalhe.
- O detalhe desconta cupom e estorno; a lista e a linha da L3 não (critério 2 da L3). Pedido de R$ 100,00 com cupom de R$ 20,00 e R$ 80,00 pagos aparece "Pago" no detalhe e com R$ 20,00 na linha "Pedidos a receber". É limite declarado da v1, com item de aceite.
- `ConsultarPedidoFerramenta.cs:68` (assistente) também usa o use case: ganha o mesmo valor e repete as 4 leituras que já faz em `:77`. Tirar a repetição fica fora.

Pedido `cancelado` não lê `aReceber`, que ali seria o total cheio: o cabeçalho mostra saldo zero e o texto de hoje de `situacaoDoPagamento` ("Cancelado com R$ X recebido", com a sobra; `dominio/pedidosCadastro.js:151`).

O botão NÃO ganha caminho novo. Ele reaproveita o `receber` da conversa (`aplicacao/api/comanda.js:271-313`). O caminho `aplicacao/pagamento.js` não existe. O detalhe roda fora do `AtendimentoProvider` (`DetalhePedido.jsx:15-17`), então o corpo do `receber` sai para uma função de `aplicacao/` que recebe o `pedidoId` e o pedido, sem ler a conversa. Conversa e detalhe chamam a mesma função. Ela mantém:

| Regra | Onde hoje |
|---|---|
| Idempotência: uma chave por envio; repetir após queda não lança duas vezes | `comanda.js:56,288`; `Idempotency-Key` em `infra/api/pagamentosPedidoApi.js:9-11`; `POST /api/pedidos/{id}/pagamentos` em `PedidosController.cs:201-203` (`EasyStock.Api/Controllers`) |
| Aprovação: pedido que espera aprovação recusa a baixa ("Aprove o pedido antes de registrar o pagamento.") | `comanda.js:283-286`; `dominio/cobranca.js:430-434` |
| Link do Mercado Pago pendente: a forma vira "na entrega" (cancela o link) e só então registra | `comanda.js:287,289`; `cobranca.js:436-438` |
| `pedido_ja_pago`: mostra a mensagem da API, não o erro genérico | `comanda.js:298`; `CobrancaPedidoConflitoException.cs:10` |
| Sem confirmação da rede: confere o pedido antes de aceitar nova tentativa | `comanda.js:299,302` |

O detalhe só tem o `PedidoResult` (`PedidoResult.cs:4-31`), sem `cobranca` nem `requerAprovacao`; a cobrança do pedido hoje só chega pela conversa (`infra/api/comandaApi.js:208`). Por isso o botão segue esta tabela, sem regra nova de valor:

| Situação | Botão | O que o detalhe mostra |
|---|---|---|
| Operacional com `aReceber` maior que zero | ligado | o saldo |
| Pago, total zero ou quitado do histórico | não aparece | "Pago" ou "Quitado" no cabeçalho |
| `cancelado` | não aparece | saldo zero e o texto de hoje de `situacaoDoPagamento` (parágrafo acima). A API recusa sempre (`RegistrarPagamentoPedidoUseCase.cs:103-110`, `PedidoStateMachine.cs:132-133`) e nada desbloqueia; devolver o que sobrou é estorno, não baixa |
| `rascunho` | desligado | "A cliente ainda não terminou este pedido no cardápio." O rascunho nasce no checkout (`CheckoutCoreService.cs:194-197`) |
| `aguardando_aprovacao_baba` | desligado | "Aprove o pedido em Entregas, no dia da entrega, antes de registrar o pagamento." Entregas tem "Aprovar" (`ConteudoEntregasApi.jsx:146`) e lista a aprovação por dia (`dominio/entregasApi.js:84`) |
| `aguardando_pagamento` | desligado | "Este pedido espera o pagamento do link ou o sinal combinado." Com o atalho, completa: "Registre pela conversa do pedido." Não diz que a API recusa: ela aceita o sinal manual da encomenda com sinal quando a cobrança pendente é só a manual (`QuitacaoPedido.cs:37-44`, `RegistrarPagamentoPedidoUseCase.cs:103-106`). Com link pendente, a conversa cancela o link antes (`comanda.js:287,289`) |

Os três pré-operacionais são os de `PedidoStateMachine.cs:112-116`, espelho em `pedidos-cockpit.js:33,143`. As linhas deles valem quando há saldo; sem saldo, vale a linha do pago.

**"Abrir conversa" só com a conversa deste pedido (#3130).** O detalhe recebe `abrirConversa` apenas para a conversa cujo pedido em andamento é este (`c.pedido?.pedidoId === pedido.id`). Sai a segunda busca por `clienteId` (`PainelPedidos.jsx:93`): ela abria a conversa da cliente com a comanda de outro pedido. Sem essa conversa, o detalhe não mostra "Abrir conversa" e o resto não muda: o botão desligado mantém o motivo da tabela. Valem as mesmas regras quando o detalhe abre pela Ficha do cliente (`FichaCliente.jsx:207`) e pela Busca na Casa (`App.jsx:742`), que já não passam `abrirConversa`: lá o botão "Registrar pagamento" funciona igual, porque a função compartilhada não lê a conversa.

Limites da v1, declarados:

- O Console só conhece o pedido em andamento de cada conversa (`aplicacao/cicloDeSincronizacao.js:91,102`). Pedido encerrado perde o atalho, embora a API saiba a conversa dele (`ConversaRepository.cs:16-19`). Expor `conversaId` no detalhe fica para depois.
- Pedido em `aguardando_pagamento` sem conversa só sai da espera pelo pagamento do link ou pelo cancelamento. Registrar à mão pelo detalhe pede ler a cobrança pelo pedido, fora desta spec.

### L2. Coluna Entrega, ordem e filtro "Entrega hoje"

Hoje `GET /api/pedidos` (`EasyStock.Api/Controllers/PedidosController.cs`) aceita `page, pageSize, status, clienteId, desde, ate, search, sort, order` (`PedidosController.cs:45-56`). `desde` e `ate` filtram `CriadoEm` (`PedidoRepository.cs:76-77`). `sort` aceita `urgencia`, `total`, `status`, `cliente` e, por padrão, `criadoem` (`PedidoRepository.cs:100-109`). A resposta já traz `agendadoParaEm` (`PedidoResult.cs:24`). O pedido não guarda o fim da janela (`Pedido.cs` só tem `AgendadoParaEm:136` e `InicioPrevistoEm:206`), mas a faixa existe fora dele: a vaga ativa liga o pedido à janela (`VagasOcupadas` com `JanelasEntrega`, `KdsPedidoQueries.cs:59-63,110`), e `AgendadoParaEm` já é o início dela (`CheckoutCoreService.cs:203`, `TrocarJanelaPedidoUseCase.cs:83`). Na v1 a coluna mostra dia e hora de início, sem o fim da faixa (#3130): o dia, a ordem e o filtro não mudam com ela, e mostrá-la pede uma leitura das vagas por página e um campo novo na lista.

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
2. Saldo maior que zero, com saldo = `Pedido.AReceberPelosPagamentos` (`Pedido.cs:452`): `Total` menos a soma dos `Pagamentos` (`TotalPago`, `:435`), e zero no pedido quitado pelo import do histórico (`QuitadoHistoricoEm`, `:129`; gravado em `LegacySnapshotImportService.cs:478`). No SQL, `QuitadoHistoricoEm == null`, como `DashboardAnalyticsQueries.cs:294`; a propriedade calculada `QuitadoHistorico` não traduz. Pago, pago a mais, total zero e quitado do histórico ficam fora. Estorno e cupom não entram na v1 (`SaldoFinanceiroPedido.cs:9,34-35` os considera; fica como limite declarado, e o detalhe da L1 os desconta: ver os efeitos na L1).
3. Sem `ContaReceber` de origem Pedido com `OrigemRefId` igual ao pedido e status diferente de `Cancelada`. Conta cancelada não está no card, então o pedido volta a contar.
4. Dia da entrega (definição da L2) antes de hoje mais 31 dias. `AgendadoParaEm` é um instante UTC: converte-se para o dia civil de Brasília (`HorarioBrasil`) ANTES de comparar com o corte do card (`FluxoCaixaQueries.cs:21`). Sem limite inferior, **decidido pelo Felipe em 2026-11-21, 23:29**, literal: "Sim, continua". Pedido já entregue e não pago segue a receber, mesmo entregue há mais de 30 dias, e pedido não tem "Vencidas". Entregue e não pago conta, como no cockpit.

**Pedido do histórico ainda aberto (#3130).** O import só aceita `aguardando`, `preparando`, `pronto`, `entregue` e `cancelado` (`LegacySnapshotImportService.cs:395`). O entregue entra quitado (`:478`) e o cancelado sai pelo critério 1. Os outros três são status operacionais, nenhum pré-operacional (`PedidoStateMachine.cs:112-116`), e entram sem pagamento de propósito ("Pagamentos antigos nao foram presumidos", `:495`); o `pronto` fica com origem `mobile-import`, os outros dois com `mobile` (`:467-468`). Decisão, inferida do código (o "Sim, continua" do Felipe tratou do limite inferior, não deste caso): eles entram na linha como qualquer pedido aberto, com o total cheio. Motivos: o import não presume pagamento; o recibo do import manda entregar o `pronto` com a forma de pagamento informada (`:152`); o cockpit já os conta. Saem da linha quando entregues no aparelho com a forma de pagamento (o `pronto` importado por `OrderLinker.cs:209-216`, o `aguardando` e o `preparando` por `:234-239`; sem duplicar o pagamento: `:580-584`), quando recebidos pela L1 ou quando cancelados.

**Contrato.** Sai pelo `GET /api/financeiro/dashboard` (`FinanceiroController.cs:18-25`). `DashboardFinanceiroDto` ganha dois campos opcionais com padrão, como `QtdContasReceberAVencer30d` (`IFluxoCaixaQueries.cs:33-49`):

- `TotalPedidosAReceber` (`decimal`, padrão `0m`)
- `QtdPedidosAReceber` (`int`, padrão `0`)

O cálculo entra em `FluxoCaixaQueries.KpisDashboardAsync` (`FluxoCaixaQueries.cs:15`), junto das parcelas vivas. Soma e contagem rodam no banco, com a soma dos `Pagamentos` em subconsulta, sem carregar pedidos em memória: sem limite inferior, o conjunto cresce com o histórico. O Console lê os campos em `PainelFinanceiro.jsx:87`, só para quem vê "receber" (`:62`, `:87`), e esconde a linha se o campo não vier (API antiga) ou se `QtdPedidosAReceber` for 0.

**Reconciliação com a ADR-0044** (`docs/adr/0044-definicoes-de-a-receber.md`). A ADR manda nomear honestamente e não unificar antes de resolver três decisões de produto. A E4 respeita:

- Não unifica. O número do card não muda, e nenhum dos quatro números da tabela da ADR muda de valor.
- A linha é a quinta medida, com rótulo próprio ("Pedidos a receber") e `title` que diz: pedidos sem conta a receber e ainda não pagos, com dia da entrega até 30 dias à frente ou em qualquer dia passado; saldo = total menos pagamentos. O rótulo não promete "próximos 30 dias".
- Responde só no escopo da linha à decisão 2 (pedido e parcela são o mesmo dinheiro?): com conta, o dinheiro é o da conta; sem conta, é o do pedido. Isso evita a soma em dobro sem unificar nada. A decisão 1 a linha resolve como o cockpit (entregue e não pago conta). A decisão 3 segue em aberto.
- A ADR cita como satélite que a conta gerada usa `Total` cheio. Desde a #2095 (F10) ela usa o saldo (`GerarContaReceberDePedidoUseCase.cs:65-69`). A PR de implementação põe um adendo datado na ADR: a quinta linha na tabela, a correção desse satélite e o satélite novo do item abaixo.
- Fica de fora da linha todo pedido com conta. O pagamento feito no pedido depois da conta gerada não é reconciliado com a parcela. A ADR não registra isso hoje (só o satélite do `Total` cheio, `0044:23,50`); o adendo o acrescenta como satélite novo (#3130). A linha não o agrava.

### 2.4. Decisões da revisão (#3130)

| Ponto | Tratamento |
|---|---|
| Pedido do histórico aberto na L3 | Decidido pelo código: entra como qualquer pedido aberto (L3, "Pedido do histórico ainda aberto"), com item de aceite |
| Detalhe sem conversa | Decidido: só a conversa deste pedido; sem ela, motivo sem atalho (L1, tabela e limites) |
| Botão no cancelado | Decidido: não aparece. Desligado com motivo só no pré-operacional com saldo |
| `SaldoFinanceiroPedido.AReceberAsync` no detalhe | Aceita: o detalhe passa a descontar cupom e estorno (L1); a linha da L3 segue sem eles, limite declarado |
| Motivo de `aguardando_pagamento` | Aceita: o texto não diz que a API recusa (tabela da L1) |
| Faixa da entrega | Fato corrigido na L2; exibir o fim da faixa fica fora da v1, com o motivo |
| Satélite do pagamento depois da conta | Aceita: entra no adendo da ADR-0044 (L3) |

## 3. Fora

- E2 (retirada, escolha de endereço, cliente nova): spec própria, já decidida, ninguém começou.
- Regra de celular: o cadastro aceita telefone fixo e celular fora do padrão que o pedido recusa para avisar no WhatsApp. É decisão de produto (exigir celular no cadastro ou só no pedido); hoje o pedido já diz o motivo.
- Cliente segue "Lead" depois do primeiro pedido: depende da regra de relacionamento da API (#2777).
- Caixa com data do dia e pagamento em dinheiro com caixa fechado: comportamento decidido (#2319, #2736), não é bug.
- Unificar os números de "a receber" (épico da ADR-0044).
- Fim da faixa de entrega na lista de Pedidos (L2).
- Atalho para a conversa de pedido encerrado (`conversaId` no detalhe) e registro à mão de `aguardando_pagamento` pelo detalhe sem conversa (L1, limites da v1).

## 4. Aceite

- [ ] T3 em Pedidos: abrir o pedido (1), "Registrar pagamento" (2), escolher o método (3), confirmar (4); o detalhe relê o pedido e mostra saldo zero. No máximo 4 cliques.
- [ ] L1 usa a função compartilhada com a conversa. Teste prova a mesma chave de idempotência no reenvio, a recusa por aprovação, o cancelamento do link e a mensagem de `pedido_ja_pago`. Cada pré-operacional com saldo mostra o botão desligado com o motivo da tabela da L1; o de `aguardando_pagamento` não diz que a API recusa. Pedido cancelado não mostra o botão: de R$ 132,30 sem pagamento, mostra saldo zero no cabeçalho, nunca R$ 132,30 a receber; com R$ 50,00 recebido, mostra "Cancelado com R$ 50,00 recebido".
- [ ] `aReceber` do detalhe pelo `SaldoFinanceiroPedido`: pedido operacional de R$ 100,00 com cupom de R$ 20,00 aplicado e sem pagamento mostra R$ 80,00 a receber, sugere R$ 80,00 e a baixa passa sem a recusa "excede o pendente". Quitado do histórico segue com zero. Teste de use case cobre os dois.
- [ ] Detalhe sem conversa (#3130): aberto em Pedidos para um pedido cuja cliente tem conversa só com OUTRO pedido em andamento, não mostra "Abrir conversa" e nunca seleciona essa conversa; com a conversa do próprio pedido, mostra. Pela Ficha do cliente e pela Busca na Casa, "Registrar pagamento" funciona e "Abrir conversa" não aparece.
- [ ] Lista de Pedidos com a data da entrega visível em 1440 e 390 px. `sort=entrega` ordena pelo dia da entrega. "Entrega hoje" lista só pedidos com entrega hoje (inclui o pedido para agora criado hoje).
- [ ] L3, exemplo verificável, loja com a flag da conta automática DESLIGADA: conta a receber de R$ 120 vencendo em 10 dias e pedido operacional sem conta, total R$ 132,30, sem pagamento, entrega em 5 dias. O card mostra R$ 120,00 e a linha mostra "Pedidos a receber: R$ 132,30 (1 pedido)".
- [ ] L3, mesma loja com a flag ligada ANTES da entrega e o pedido já entregue: ele ganha a conta de R$ 132,30. O card passa a R$ 252,30 (conta com vencimento em 7 dias, dentro dos 30) e o pedido NÃO aparece na linha, que some por zerar.
- [ ] L3, exclusões: pedido cancelado, pré-operacional, pago, quitado do histórico, com entrega a partir de hoje+31 ou com conta não cancelada (inclusive a paga) não entra na linha. Pagamento parcial entra só com o saldo. Pedido com conta cancelada volta à linha.
- [ ] L3, borda de fuso: pedido agendado às 21h de Brasília em hoje+30 (já é hoje+31 em UTC) conta como hoje+30 e ENTRA na linha; às 21h de hoje+31 NÃO entra.
- [ ] L3, pedido antigo (decisão do Felipe, 2026-11-21): pedido operacional sem conta, entregue há 45 dias, total R$ 80,00, sem pagamento, ENTRA na linha com R$ 80,00; o card das contas não muda.
- [ ] Pedido do histórico ainda aberto (#3130): importado como `pronto`, R$ 80,00, sem pagamento e sem conta, entrega há 10 dias, ENTRA na linha com R$ 80,00. Depois de "Registrar pagamento" de R$ 80,00 no detalhe, sai da linha.
- [ ] Quitado do histórico (#2813): pedido entregue de R$ 80,00, sem pagamento e sem conta, com `QuitadoHistoricoEm` preenchido pelo import, NÃO entra na linha da L3. No detalhe, aparece como quitado e sem "Registrar pagamento".
- [ ] `GET /api/financeiro/dashboard` sem os campos novos (API antiga) não quebra o Console: a linha não aparece.
- [ ] Prova Red→Green por lacuna e roteiro de navegador com as 4 tarefas, antes e depois.
- [ ] `npm run qualidade` verde; CI verde; `/code-review` sem Critical.

## 5. Tier e Rollback

Tier alto, pela natureza: feat sem spec aprovada, contrato de API novo (`/api/pedidos` e `/api/financeiro/dashboard`, ambos só aditivos e opcionais) e adendo em ADR (processo). PR aberta até a label `aprovado`.

Rollback: reverter a PR. Sem migração: L2 e L3 só consultam, sem coluna nem índice novo. Cliente antigo ignora os campos e parâmetros novos. Console novo contra API antiga esconde a linha da L3 e perde só a ordem e o filtro de entrega. O adendo da ADR volta junto na reversão.
