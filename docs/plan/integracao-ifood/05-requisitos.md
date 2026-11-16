# 5. Requisitos

Rastreio: UC em [06](06-casos-de-uso.md), lacuna L em [04 §4.6](04-mapeamento.md#46-lacunas), decisão D em [08](08-duvidas.md). Siglas de URL no [README](README.md).
Fase: 1 = pedidos (escopo recomendado em D02); 2 = disponibilidade e loja; 3 = catálogo completo.

## 5.1 Funcionais

| Id | Requisito | Fase | UC | Origem |
|---|---|---|---|---|
| RF01 | Vincular uma loja do EasyStok a um merchantId do iFood e desvincular | 1 | UC01, UC02 | L14; G/first-steps/request-access |
| RF02 | Obter token e renovar só ao expirar | 1 | UC03 | U/authentication/centralized; U/order/homologation |
| RF03 | Consumir eventos a cada 30 s (ou webhook, conforme D04), persistir, deduplicar por `id`, ordenar por `createdAt` e dar ack de todos depois de persistir | 1 | UC10 | U/events/polling-overview; L16 |
| RF04 | Importar o pedido no PLC: buscar o detalhe com retry até 10 min, criar `Pedido` com origem `ifood`, id externo, `displayId`, tipo, quem entrega e marca de teste | 1 | UC11 | U/order/workflow; L01, L02, L06 |
| RF05 | Traduzir itens pelo `externalCode` para `CardapioItem` (o relink #1619 resolve o produto); item não mapeado entra sem produto e gera alerta | 1 | UC11, UC30 | L07, L10 |
| RF06 | Guardar complementos de cada item e exibi-los no KDS e na comanda | 1 | UC11, UC24 | L05; U/order/homologation |
| RF07 | Registrar valores: subtotal, taxa de entrega, desconto da loja, subsídio do iFood, taxas do iFood, pago online, a cobrar na entrega, troco, bandeira | 1 | UC11, UC24 | L03, L04, L17; U/order/details |
| RF08 | Exibir o pedido novo com alerta e contagem do prazo de 8 min de aceite | 1 | UC12 | L08; U/order/fundamentals |
| RF09 | Confirmar o pedido: manual, ou automático se a loja ligar a opção (D05) | 1 | UC12 | U/order/endpoints |
| RF10 | Recusar ou cancelar pela loja com motivo escolhido da lista de `cancellationReasons` | 1 | UC13 | U/order/cancellation; U/order/homologation |
| RF11 | Informar preparo, pronto e despacho ao iFood quando o estado mudar no EasyStok; despacho só se `deliveredBy=MERCHANT` | 1 | UC14, UC15, UC16 | U/order/endpoints |
| RF12 | Aplicar eventos vindos do iFood: CFM, CON, CAN, CARF, CLT, inclusive mudanças feitas em outro sistema (Gestor de Pedidos) | 1 | UC17, UC18, UC21, UC23 | U/order/homologation; U/events/polling-overview |
| RF13 | Cancelar o pedido por evento do canal, sem usuário e sem a regra de nível Gerente, devolvendo estoque, sem gerar "devolução a fazer" e sem saldo a devolver no `SaldoFinanceiroPedido` | 1 | UC18 | L19; `CancelarPedidoUseCase.cs:66-71`, `:115-134` |
| RF14 | Mostrar a disputa (HSD) com prazo e permitir aceitar, rejeitar ou contrapropor; registrar o desfecho (HSS) | 1 | UC19 | L20; U/order/handshake-platform |
| RF15 | Aplicar alteração de itens (OPA) e reajustar estoque se já baixado | 1 | UC20 | L21; U/order/events |
| RF16 | Imprimir comanda com observações, complementos, pagamento, troco, cupom e quem paga, código de coleta e documento para NF; sem CPF e endereço no documento do entregador | 1 | UC24 | L22; U/order/homologation; U/order/details |
| RF17 | Baixar estoque do pedido iFood pela regra atual (em Pronto) e devolver no cancelamento | 1 | UC15, UC18 | `PedidoEstoqueIntegrationService.cs`; D08 |
| RF18 | Separar o iFood em relatórios por canal | 1 | UC11 | L18 |
| RF19 | Mostrar o estado da integração por loja: token, último polling, último erro, status da loja no iFood com validações (leitura de Merchant) | 1 | UC02, UC40, UC50 | L12; G-mer |
| RF20 | Empurrar disponibilidade (AVAILABLE ou UNAVAILABLE) quando `CardapioItem.Disponivel` ou a oferta mudar | 2 (se D12=A) | UC33 | L11; OA |
| RF21 | Pausar e reabrir a loja no iFood a partir do controle manual do expediente | 2 | UC41 | L12; G-mer |
| RF22 | Enviar a grade de horário do expediente | 2 | UC42 | L24; G-mer |
| RF23 | Atualizar preço no iFood em lote e conferir o resultado do lote | 3 | UC32 | OA; G-cat/errors-and-troubleshooting |
| RF24 | Criar e manter itens, categorias e complementos pelo EasyStok (`PUT /items`) | 3 (só se D11=B) | UC31 | OA; HC |
| RF25 | Enviar saldo de estoque (`/inventory`) | 3 (só se D12=B) | UC34 | OA |
| RF26 | Tratar cancelamento solicitado (CAR) e mudança de endereço (DAR) | 1 | UC13, UC25 | U/order/events |
| RF27 | Gravar e dar ack de eventos sem uso (RPS, DRCR, DGAC) e de `code` desconhecido, sem falhar | 1 | UC10, UC26 | U/order/events; best-practices |
| RF28 | Fluxo de retirada e consumo no local (TAKEOUT, DINE_IN) sem despacho | 1 | UC27 | U/order/details |
| RF29 | Validar o código de coleta do entregador | 1 | UC22 | U/order/endpoints |
| RF30 | Reconciliar pedidos abertos depois de parada e registrar o que foi feito no Gestor de Pedidos | 1 | UC10, UC52 | U/events/polling-overview; U/order/endpoints |
| RF31 | Alertar quando a fila de pedidos do iFood passar do limite configurado da loja (D15) | 1 | UC53 | L23 |
| RF32 | Roteiro de homologação de Order e Events como teste de aceite | 1 | UC51 | U/order/homologation |

## 5.2 Não funcionais

| Id | Requisito | Medida | Origem |
|---|---|---|---|
| RNF01 | Disponibilidade do consumidor de eventos | Polling a cada 30 s sem interrupção; o EasyStok fora tira a loja do ar no iFood | U/events/polling-overview; G-mer |
| RNF02 | Prazo de aceite | Pedido visível na operação em menos de 60 s após o PLC (meta interna; o limite do iFood é 8 min) | U/order/fundamentals |
| RNF03 | Idempotência | Evento processado uma vez por `id`; pedido criado uma vez por `orderId` (índice único) | U/events/polling-overview; L01 |
| RNF04 | Persistir antes do ack | Nenhum ack sem gravação; o iFood reenvia evento sem ack por até 8 h após a entrega | U/events/polling-overview |
| RNF05 | Isolamento multi-tenant | Evento sem JWT resolve a empresa pelo merchantId antes de qualquer leitura; RLS ativo; eventos em tabela com `EmpresaId` (L15) | ADR-0010; `WebhookLalamoveController` |
| RNF06 | Segredo | clientSecret no cofre cifrado ou em segredo do servidor; nunca em log | `CredencialIntegracao.cs`; L13 |
| RNF07 | Webhook (se D04 usar) | 202 em até 5 s; assinatura validada em tempo constante antes do parse | U/events/webhook-signature; U/events/webhook-request |
| RNF08 | Limites | Respeitar 6000 req/min no polling e os limites de Catalog e Merchant; tratar 429 com backoff | U/events/polling-overview; RL |
| RNF09 | Taxa de erro e ack | Em 24 h: abaixo de 1% em polling e `/status`; 2% em `/orders/{id}`, `/confirm`, `/requestCancellation`, `/merchants` e `/interruptions`; ack de 95% ou mais; nunca polling abaixo de 30 s | improper-use |
| RNF10 | Retry | Backoff (imediato, 1 s, 2 s, 4 s); não repetir 4xx salvo 429; 404 do detalhe até 10 min | G-cat/errors-and-troubleshooting; U/order/workflow |
| RNF11 | Exclusão entre instâncias | Um único consumidor por token (advisory lock, como `CorridasLalamoveJob.cs:16`) | [base §1.2](fontes/easystok-base.md) |
| RNF12 | Tolerância a mudança | Aceitar enums e campos novos sem falhar; guardar o JSON bruto do pedido | best-practices; U/order/details |
| RNF13 | Privacidade (LGPD) | Não guardar segmentação; telefone com localizador expira; documento só para NF | U/order/details |
| RNF14 | Fuso | Datas do iFood em UTC; exibir em America/Sao_Paulo | U/order/events; `OperacionalFuso` |
| RNF15 | Dinheiro | Decimal (numeric 14,2); conferir `orderAmount` com a soma interna e alertar divergência | U/order/details |
| RNF16 | Observabilidade | Log estruturado por empresa, merchantId, orderId e eventId; alerta no `VigiaIntegracoes` | `VigiarIntegracoesUseCase` |
| RNF17 | Catálogo (fase 3) | Preço e status refletidos em até 2 s; 100+ itens em até 10 s; timeout de 30 s | HC |
| RNF18 | Divergências da doc | Cada caminho, verbo e campo divergente provado na loja de teste antes do código | [02 §2.8](02-api.md#28-divergências-na-própria-documentação) |
| RNF19 | Sem eco de status | Mudança que veio do iFood não volta ao iFood: o consumidor do outbox filtra pela `Origem` do `pedido.mudou_status` | `AtualizarStatusPedidoUseCase.cs:166-172` |
