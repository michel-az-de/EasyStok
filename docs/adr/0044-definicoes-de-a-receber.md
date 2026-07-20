# ADR-0044 — Definições honestas de "a receber" (Dashboard/Pedidos/Financeiro)

- Status: Aceito
- Data: 2026-07-19
- Relacionado: #958/#959 (BUG-003), #962 (BUG-002), épico #868 (Esteira de Pedidos), épico #956 (auditoria QA)

## Contexto

QA (2026-07-19) reportou BUG-005: três telas mostram números diferentes para "a receber/pendências" no mesmo instante — Dashboard "PENDENTES R$ 1.547,44", Pedidos (cockpit) "A RECEBER R$ 2.626,56", Financeiro "A RECEBER 30D R$ 33,34" + "Vencidas R$ 1.066,66".

Investigação mostrou que **não é um bug de cálculo** — são **quatro medidas genuinamente diferentes**, sobre entidades e janelas distintas, cada uma correta para a pergunta que responde:

| # | Onde | Fonte | Predicado | Janela | Inclui entregue? |
|---|---|---|---|---|---|
| 1 | Dashboard "Pendentes" | `Pedido.Total` (bruto, `AnalyticsRepository.ResumoDia`) | não-entregue e não-cancelado | nenhuma (histórico inteiro) | Não (por definição — "pendente" = ainda não entregue) |
| 2 | Pedidos (cockpit) "A receber" | `Pendente = Total - TotalPago` das linhas carregadas (`pedidos-cockpit.js:92`) | não-cancelado, não-pré-operacional | cap de página (200) | Sim |
| 3 | Financeiro "A Receber 30d" | `ParcelaReceber` (`FluxoCaixaQueries`) — entidade **diferente** de Pedido | não-paga, não-cancelada | vencimento em 30 dias | N/A (não é pedido) |
| 4 | `DashboardAnalyticsQueries` (30 dias, não exibido hoje) | `Pedido` com `Pagamentos` carregado corretamente | não-cancelado, `TotalPago < Total` | 30 dias de criação | Sim |

Unificar num número único exigiria resolver três decisões de produto ainda em aberto:

1. Pedido **entregue** e não pago conta como "a receber"? (o cockpit diz sim, o Dashboard diz não, por design)
2. `Pedido` e `ParcelaReceber` são o mesmo dinheiro? (`GerarContaReceberDePedidoUseCase` é opt-in por loja e hoje usa `Total` cheio, ignorando `TotalPago` — gera double-count quando ativo, tratado como bug satélite, não parte desta decisão)
3. Qual janela é a canônica: sem janela, 30d de criação, ou 30d de vencimento?

Sem essas respostas, um "serviço único de a-receber" só move a divergência para dentro de uma classe — não a resolve.

## Decisão

**Nomear honestamente agora, unificar depois.** Manter os quatro números (a unificação vira épico separado, condicionado às três decisões acima), mas:

- Rotular cada tela pelo que ela **de fato mede**, não por um rótulo genérico que sugere serem a mesma coisa.
- Documentar a tabela acima como referência única (este ADR).
- Corrigir bugs *mecânicos* encontrados no caminho (não decisões): o card "Vencidas" do Financeiro somava `TotalVencidoPagar + TotalVencidoReceber` num número só, com link que só abria contas a pagar — isso é bug, corrigido separadamente em #375 (fatia C1), não decisão de nomenclatura.

## Consequências

- Os quatro números continuam divergindo — o ADR não resolve BUG-005 "matematicamente", resolve a **confusão** sobre o que cada um significa.
- Rótulos mudam (Dashboard "Pendentes" → "A entregar"; Pedidos "A receber" → "A receber (nesta lista)"; Financeiro ganha `title` explicativo) — nenhum valor numérico muda.
- Fica registrado como pré-requisito para o épico de unificação (sob #868 ou #956): não iniciar sem resolver as três decisões de produto listadas em Contexto.

## Alternativas consideradas

- **Serviço único `IRecebiveisQueries` agora**: rejeitada — as três decisões de produto não estão resolvidas; implementar sem elas apenas esconderia a divergência atrás de uma abstração, sem corrigi-la de fato.
- **Escolher uma das quatro como "a" fonte de verdade e esconder as outras**: rejeitada — cada uma responde uma pergunta operacional real (o Dashboard quer saber "quanto ainda vou entregar", o Financeiro quer saber "quanto vence em 30 dias"); esconder perderia informação que times diferentes usam.

## Issues satélite (não implementadas neste ADR)

- Épico de unificação sob #868/#956, condicionado às 3 decisões de produto acima.
- `GerarContaReceberDePedidoUseCase` usa `Total` cheio em vez de `Total - TotalPago` ao gerar `ContaReceber` — double-count quando a geração automática está ligada por loja.
