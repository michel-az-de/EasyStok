# API iFood: pedidos (fonte de pesquisa)

Acesso: 2026-11-16. Portal oficial: https://developer.ifood.com.br (Gatsby/SPA). O WebFetch devolveu 404 ou vazio. O conteúdo foi lido só pelo navegador embutido. Base da API: https://merchant-api.ifood.com.br. Versões na referência (https://developer.ifood.com.br/pt-BR/docs/references): Authentication v1.0, Events v1.0, Order v1.0, Merchant v1.0. Changelog da doc: [NÃO CONFIRMADO]. Último item visível do changelog na home é de 03/11/2026 (Catalog), sem relação com Order.

Prefixos usados nas tabelas:
- U = https://developer.ifood.com.br/pt-BR/docs/food/guides/modules
- G = https://developer.ifood.com.br/pt-BR/docs/getting-started

## 1. Parceiro, app e homologação

| Item | Conteúdo | URL |
|---|---|---|
| Conta | O cadastro cria automaticamente uma loja de teste e um app de teste | G/first-steps/create-account |
| Tipos de app | Centralizado (SaaS, acesso a todas as lojas) e Distribuído (on-premises, uma loja). Visibilidade pública ou privada. Categorias: PDV, Groceries, Catálogo, Logística, Avaliações, Finanças, BI | G/first-steps/create-app |
| App de produção | Criado só depois de homologar o app de teste. Menu Apps > Novo Aplicativo | G/first-steps/create-app |
| Credenciais | clientId e clientSecret em Meus Apps > Credenciais do aplicativo | U/authentication/centralized |
| Vínculo, centralizado | Meus Apps > Permissões, buscar a loja por ID ou CNPJ, o lojista aprova no Portal do Parceiro, gerar novo accessToken | G/first-steps/request-access |
| Vínculo, distribuído | O app gera um userCode e mostra verificationUrlComplete. O lojista autoriza no Portal do Parceiro e recebe o authorizationCode, que o app troca por token. Sem navegador: "Ativar aplicativo via código" no Portal | G/first-steps/request-access |
| Pré-requisito do vínculo | Homologação concluída | G/first-steps/request-access |
| Pedido de teste | Automático (Portal > Testes > Gerar pedido de teste) ou manual no app iFood (endereço "Ramal Bujari, 100", Modo Teste). Cupons: VOUCHER_IFOOD, VOUCHER_REST, VOUCHER_ENTGRATIS. Lojas de teste só aparecem abertas se houver polling | G/first-steps/generate-test-order |
| Rede | Liberar HTTPS 443 de saída para merchant-api.ifood.com.br. Alguns provedores de hospedagem são bloqueados na CDN (403) e exigem chamado | G/first-steps/create-app |
| Custo | [NÃO CONFIRMADO]. A FAQ lista "Preciso pagar para utilizar as APIs do iFood?", mas a resposta não foi lida | https://developer.ifood.com.br/pt-BR/support/faq |

### Homologação do Order e do Events

Fonte: U/order/homologation. Os módulos Order e Events são homologados juntos.

| Tópico | Regra |
|---|---|
| Pré-requisitos | Conta Profissional com CNPJ (CPF não é aceito, confirmado em U/events/polling-overview). App de produção da categoria PDV, credenciais de teste, loja de teste |
| Quem inicia | Só o dono do app, em Portal > Homologação > Nova homologação |
| Seleções | App, módulos (Order e Events obrigatórios; Catalog e Review via chamado), loja, Polling ou Webhook |
| Etapa 1 | Conectividade: "Testar conexão" |
| Etapa 2 | Receber e confirmar pedidos, com POST acknowledgment de todos os eventos |
| Etapa 3 | Cancelamento: GET cancellationReasons e cancelar |
| Etapa 4 | Despacho: em entrega própria, readyToPickup e dispatch |
| Etapa 5 | Conclusão do pedido |
| Evidência | Até 5 capturas de tela por etapa |
| Limite | 1 homologação por app a cada 4 horas |
| Resultado | Aprovada passa por análise de especialistas. Rejeitada traz relatório e permite nova tentativa após 4 h |

## 2. Autenticação

| Item | Valor | URL |
|---|---|---|
| Endpoint de token | POST https://merchant-api.ifood.com.br/authentication/v1.0/oauth/token, `application/x-www-form-urlencoded` | U/authentication/centralized |
| Centralizado | grantType=client_credentials, clientId, clientSecret. Sem refresh token | U/authentication/centralized |
| userCode (distribuído) | POST .../authentication/v1.0/oauth/userCode (clientId). Retorna userCode, authorizationCodeVerifier, verificationUrl, verificationUrlComplete, expiresIn=600 | U/authentication/distributed |
| Token (distribuído) | grantType=authorization_code (authorizationCode, authorizationCodeVerifier) ou grantType=refresh_token (refreshToken) | U/authentication/distributed |
| Validade | expiresIn=21600 s (6 h). Tokens não excedem 8.000 caracteres | U/authentication/distributed |
| Resposta 200 | accessToken (JWT), type (bearer), expiresIn | U/authentication/distributed |
| Erros | 401 `error.code=Unauthorized` ("Bad credentials"); 500 `InternalServerError` | U/authentication/distributed |
| Uso do token | Header `Authorization: Bearer <token>` | U/authentication/centralized |
| Renovação | Só quando expirado, não antecipadamente | U/order/homologation |
| Revogação | Só o usuário que autorizou pode revogar (distribuído) | U/authentication/distributed |
| Escopos finos | [NÃO CONFIRMADO]. Só se escolhem "módulos" no app | G/first-steps/create-app |

## 3. Events

### 3.1 Polling, acknowledgment e webhook

| Item | Valor | URL |
|---|---|---|
| Polling | GET https://merchant-api.ifood.com.br/events/v1.0/events:polling. 200 traz lista, 204 sem eventos, 400 (muitos merchants), 403 (retorna `unauthorizedMerchants`), 429 | U/events/polling-overview |
| Query | `categories` (padrão FOOD e GROCERY; ALL para todas), `types`, `groups` | U/events/polling-overview |
| Header | `x-polling-merchants` (lista de merchantIds). Até 100 por requisição. Obrigatório se o token tiver mais de 500 merchants | U/events/polling-overview |
| Frequência | A cada 30 s. Mantém o merchant online. Sem polling, a loja perde o status online | U/events/polling-overview |
| Rate limit | 6000 RPM por token (429) | U/events/polling-overview |
| Throttling | Cada evento com 50 entregas sem ACK gera 1 strike (TTL de 30 min). 100 strikes bloqueiam o polling por 5 min, seguidos de cooldown de 60 min. Previsto a partir de 02/04/2026 | U/events/polling-overview |
| Retenção | Até 8 horas após a entrega do pedido | U/events/polling-overview |
| Ordem e duplicidade | A API pode entregar fora de ordem (ordenar por `createdAt`) e repetir eventos, inclusive PLACED. Deduplicar por `id` e dar ack mesmo assim | U/events/polling-overview |
| Filtros | Eventos fora do filtro recebem auto-ack. Trocar o filtro depois perde os eventos já confirmados | U/events/polling-overview |
| Vários devices | Cada app tem controle de ack independente. Eventos de outros devices (ex.: Gestor de Pedidos) também chegam | U/events/polling-overview |
| Ack | POST https://merchant-api.ifood.com.br/events/v1.0/events/acknowledgment. Corpo `[{"id":"..."}]`. Resposta 202. Também 400, 403, 413, 500 | U/events/polling-overview |
| Regras do ack | Enviar para todo polling que retornar 200, inclusive eventos não usados. Só uma vez por evento. Enviar após persistir o evento | U/events/polling-overview |
| Webhook, disponibilidade | Só para app centralizado | U/events/webhook-overview |
| Webhook, cadastro | Meus Apps > [app] > Webhook, habilitar e informar 1 URL HTTPS (máx. 250 caracteres) | U/events/webhook-overview ; U/events/webhook-request |
| Webhook, resposta | 202 Accepted em até 5 s, com processamento assíncrono | U/events/webhook-overview |
| Webhook, assinatura | Header `X-IFood-Signature` = HMAC-SHA256 em hex do corpo bruto, com o client_secret. Comparar em tempo constante e validar antes do parse. Rejeitar assinatura inválida (testado na homologação) | U/events/webhook-signature |
| Webhook, retentativa | Reenvio após 30 s e depois com intervalo crescente. Descarta o evento após 15 min. Garantia at-least-once, sem ordem, sem ack explícito, sem filtros | U/events/webhook-request |
| Webhook, bloqueio | E-mails de alerta. Desabilitado após 72 h sem correção. Usar polling como fallback | U/events/webhook-request |
| Webhook, presença | O app registrado recebe eventos de presença; marca merchants online por app ou por merchant | U/events/webhook-overview |
| IPs do iFood | Lista de IPs para allowlist em `webhook-best-practices` (não lida) | U/events/webhook-overview |

### 3.2 Códigos de evento

Fonte: U/order/events. Estrutura comum: `id`, `code`, `fullCode`, `orderId`, `merchantId`, `salesChannel`, `createdAt` (UTC), `category`, `metadata`.

| code | fullCode | Significado e ação |
|---|---|---|
| PLC | PLACED | Novo pedido. Confirmar em até 8 min |
| CFM | CONFIRMED | Pedido confirmado |
| CAN | CANCELLED | Cancelado. `metadata`: CANCEL_CODE, CANCEL_USER, CANCEL_STAGE, CANCEL_ORIGIN, CANCEL_REASON, CANCEL_CODE_DESCRIPTION, TIMEOUT_EVENT, CANCELLATION_DISPUTE, CANCELLATION_OCCURRENCE |
| CAR | CANCELLATION_REQUESTED | Cancelamento solicitado. `metadata`: ORIGIN (ex.: SYSTEM), details, reason_code |
| CARF | CANCELLATION_REQUEST_FAILED | Cancelamento rejeitado. `metadata`: CANCEL_CODE, CANCELLATION_REQUEST_FAILED_REASON |
| SPS | SEPARATION_STARTED | Só GROCERY |
| SPE | SEPARATION_ENDED | Só GROCERY |
| RTP | READY_TO_PICKUP | Pronto para retirada (só FOOD) |
| DSP | DISPATCHED | Despachado ou retirado |
| CON | CONCLUDED | Entregue, retirado ou concluído por timeout |
| PRS | PREPARATION_STARTED | Preparo iniciado (só FOOD) |
| RPS | RECOMMENDED_PREPARATION_START | Início de preparo sugerido (Preparo Inteligente, só FOOD) |
| ADR | ASSIGN_DRIVER | Entregador iFood atribuído. Libera o `/tracking` |
| GTO | GOING_TO_ORIGIN | Entregador a caminho da loja |
| AAO | ARRIVED_AT_ORIGIN | Entregador na loja |
| CLT | COLLECTED | Entregador coletou |
| AAD | ARRIVED_AT_DESTINATION | Entregador no destino |
| DRGO | DELIVERY_RETURNING_TO_ORIGIN | Devolução em curso |
| DRDO | DELIVERY_RETURNED_TO_ORIGIN | Pedido devolvido à loja |
| DRCR | DELIVERY_RETURN_CODE_REQUESTED | Código de devolução solicitado |
| HSD | HANDSHAKE_DISPUTE | Disputa do cliente. Responder antes de `expiresAt` |
| HSS | HANDSHAKE_SETTLEMENT | Disputa resolvida |
| OPA | ORDER_PATCHED | Itens alterados após confirmação (ADD_ITEMS, EDIT_ITEMS, DELETE_ITEMS). Atualizar comanda e faturamento |
| DAR | DELIVERY_ADDRESS_CHANGE_REQUESTED | Cliente mudou o endereço |
| DGAC | DELIVERY_GROUP_ASSOCIATED | Entrega agrupada. Substitui DELIVERY_GROUP_ASSIGNED, deprecado em 13/10/2025 |

### 3.3 Cancelamento pelo cliente ou plataforma e Handshake

Fontes: U/order/cancellation e U/order/handshake-platform.

| Item | Valor |
|---|---|
| Cancelamento iniciado pelo cliente | Em geral negociado via HSD e HSS |
| Cancelamento pelo iFood | Automático (sem confirmação no prazo) ou manual pelo atendimento. Chega como CAN com o motivo no metadata |
| Cancelamento parcial | Chega como OPA: CHANGE_ITEMS (quantidade ou unidade) e DELETE_ITEMS |
| HSD `action` | CANCELLATION, PARTIAL_CANCELLATION, PROPOSED_AMOUNT_REFUND, PROPOSED_ADDITIONAL_TIME, VOID |
| HSD `handshakeType` | AFTER_DELIVERY, DELAY, PREPARATION_TIME, AFTER_DELIVERY_PARTIALLY |
| HSD `timeoutAction` | ACCEPT_CANCELLATION, REJECT_CANCELLATION, VOID. Executada se não houver resposta até `expiresAt`, gerando HSS com status EXPIRED |
| HSD `alternatives[].type` | REFUND, BENEFIT, ADDITIONAL_TIME |
| HSS `status` | ACCEPTED, REJECTED, EXPIRED, ALTERNATIVE_REPLIED |
| Aceitar | POST https://merchant-api.ifood.com.br/order/v1.0/disputes/{disputeId}/accept. Corpo: `reason` (de `metadata.acceptCancellationReasons`), `detailReason` (máx. 250). Resposta 201 |
| Rejeitar | POST .../disputes/{disputeId}/reject. Corpo: `reason` (negotiationReasons). Resposta 201 |
| Contraproposta | POST .../disputes/{disputeId}/alternative. Corpo: `type` (REFUND, BENEFIT ou ADDITIONAL_TIME) e `metadata` (amount, additionalTimeInMinutes, reason). Resposta 201 |
| Erros do handshake | 404 DISPUTE_NOT_FOUND, 422 DISPUTE_ALREADY_ANSWERED, 400 INVALID_REASON, 400 INVALID_AMOUNT |
| Regra | Uma resposta por `disputeId`. Sem resposta há ação automática (geralmente cancelamento ou reembolso) |
| Guia de implementação | `handshake-platform-guide`: [NÃO CONFIRMADO], não lido |

## 4. Order

Base: https://merchant-api.ifood.com.br/order/v1.0. Fontes: U/order/endpoints, U/order/workflow, U/order/cancellation, U/order/fundamentals.

### 4.1 Endpoints

| Ação | Método e caminho | Resposta |
|---|---|---|
| Detalhes | GET /orders/{id} | 200. 404 se ainda indisponível (retry com backoff até 10 min) ou pedido com mais de 7 dias |
| Virtual bag | GET /orders/{id}/virtual-bag | Só grocery. Ver https://developer.ifood.com.br/pt-BR/docs/references |
| Confirmar | POST /orders/{id}/confirm | 202. Resultado vem como CFM ou CARF |
| Iniciar preparo | POST /orders/{id}/startPreparation | 202 |
| Pronto | POST /orders/{id}/readyToPickup | 202. Obrigatório em TAKEOUT, DINE_IN e DELIVERY |
| Despachar | POST /orders/{id}/dispatch | 202. Só entrega própria, depois do readyToPickup |
| Motivos | GET /orders/{id}/cancellationReasons | 200 `{reasons:[{code,description}]}` |
| Cancelar | POST /orders/{id}/requestCancellation, corpo `{"reason":"501"}` | 202. Efetivo só com o evento CAN |
| Rastrear | GET /orders/{id}/tracking | latitude, longitude, expectedDelivery, pickupEtaStart, deliveryEtaEnd, trackDate. Só entrega iFood, 1 req/30 s |
| Validar coleta | POST /orders/{id}/validatePickupCode `{"code"}` | `{valid:true}`. Só entrega iFood, se habilitado |
| Validar entrega | POST /orders/{id}/verifyDeliveryCode `{"code"}` | `{valid:true}` e o pedido vira CONCLUDED |
| Receita médica | GET /orders/{id}/prescriptions/{prescriptionId} | Binário (png, jpeg, pdf) |

Observação: na entrega própria, o lojista compartilha com o entregador o link https://confirmacao-entrega-propria.ifood.com.br/ (U/order/workflow).

### 4.2 Ciclo de vida e prazos

| Tema | Regra | URL |
|---|---|---|
| Ciclo FOOD | PLACED, CONFIRMED, PREPARATION_STARTED (opcional), READY_TO_PICKUP, DISPATCHED, CONCLUDED, CANCELLED | U/order/workflow |
| Confirmar | Em 8 min: IMMEDIATE após `createdAt`; SCHEDULED após `preparationStartDateTime`. Senão cancela automaticamente e há penalidade | U/order/fundamentals |
| Conclusão automática | Restaurante com entrega iFood: entrega ou 6 h. Restaurante com entrega própria: 4 h. Mercado e farmácia: 13 h. Pedido agendado conta a partir de `scheduling.to` | U/order/workflow |
| Cancelamento excessivo | Pode fechar a loja temporariamente | U/order/cancellation |
| Pedido agendado e retirada | Páginas `scheduled-orders` e de pedidos para retirar: [NÃO CONFIRMADO], não lidas | |

### 4.3 Motivos de cancelamento

Fonte: U/order/cancellation. Existem outros códigos de uso exclusivo do iFood. Tratar `reason` como string e usar a lista devolvida por `/cancellationReasons`.

| Código | Motivo |
|---|---|
| 501 | PROBLEMAS DE SISTEMA |
| 502 | PEDIDO EM DUPLICIDADE |
| 503 | ITEM INDISPONÍVEL |
| 504 | RESTAURANTE SEM MOTOBOY |
| 505 | CARDÁPIO DESATUALIZADO |
| 506 | PEDIDO FORA DA ÁREA DE ENTREGA |
| 507 | CLIENTE GOLPISTA / TROTE |
| 508 | FORA DO HORÁRIO DO DELIVERY |
| 509 | DIFICULDADES INTERNAS DO RESTAURANTE |
| 511 | ÁREA DE RISCO |
| 512 | RESTAURANTE ABRIRÁ MAIS TARDE |

Erros de `requestCancellation`: 400 InvalidParameter, 400 OrderExceededCancellationDeadline, 400 OrderHasACancellationInProgress, 400 (pedido já cancelado), 404 OrderNotFound, 204 (nenhuma política encontrada). Corpo: `{"code","message"}`.

### 4.4 Estrutura do JSON do pedido

Fonte: U/order/details. A doc não marca campos obrigatórios, só "opcional" em alguns. Em `/orders/{id}` o `status` não foi confirmado como campo do detalhe (o exemplo de U/order/endpoints mostra `status` e `scheduling`, que divergem de U/order/details).

| Campo | Tipo e valores | Nota |
|---|---|---|
| id | uuid | |
| displayId | string | ID amigável |
| createdAt | data | |
| preparationStartDateTime | data | Horário recomendado para iniciar o preparo |
| isTest | boolean | |
| extraInfo | string | |
| orderType | DELIVERY, TAKEOUT, DINE_IN | |
| orderTiming | IMMEDIATE, SCHEDULED | |
| salesChannel | IFOOD, DIGITAL_CATALOG, POS, TOTEM, IFOOD_SHOP, GROCERY_WHITELABEL | |
| category | FOOD, GROCERY, ANOTAI, FOOD_SELF_SERVICE | |
| merchant | id, name, business.name | |
| customer | id, name, documentNumber, documentType (CPF ou idEstrangeiro), ordersCountOnMerchant, phone.number, phone.localizer, phone.localizerExpiration, segmentation, billingAddress | `documentNumber` é opcional e só para NF. `phone` é opcional e deixa de ser enviado 3 h após a entrega. Segmentação é confidencial |
| items[] | index, id, uniqueId, imageUrl, externalCode, ean, name, type, quantity, unit (UN, g, kg, ml, L), unitPrice, price, scalePrices, optionsPrice, customizationPrice, totalPrice, observations, options[] | `externalCode` opcional. `observations` até 1024 caracteres. `price` = quantidade x (unitário + adição) |
| items[].options[] | index, id, name, groupName, type, externalCode, quantity, unit, unitPrice, addition, price, customization[] | Complemento (2º nível) |
| ...options[].customization[] | id, name, groupName, externalCode, type, quantity, unitPrice, addition, price | 3º nível |
| items[].scalePrices | defaultPrice, scales[{price, minQuantity}] | Preço de atacado |
| benefits[] | value, target, targetId, sponsorshipValues[{name, value, description}], campaign{id, name, description} | `target`: CART, DELIVERY_FEE, ITEM, PROGRESSIVE_DISCOUNT_ITEM |
| total | subTotal, deliveryFee, benefits, additionalFees, orderAmount | orderAmount = subTotal + deliveryFee + additionalFees - benefits |
| additionalFees[] | type (SMALL_ORDER_FEE), description, fullDescription, value, liabilities[{name, percentage}] | Receita do iFood, não entra na NF |
| payments.prepaid, payments.pending | double | prepaid = ONLINE; pending = OFFLINE (cobrar na entrega) |
| payments.methods[] | value, currency, type (ONLINE, OFFLINE), method, card.brand, wallet.name, cash.changeFor, transaction.authorizationCode, transaction.acquirerDocument | `method`: CASH, CREDIT, DEBIT, MEAL_VOUCHER, FOOD_VOUCHER, GIFT_CARD, DIGITAL_WALLET, PIX, OTHER. Troco em `cash.changeFor` |
| delivery | mode (DEFAULT, EXPRESS, HIGH_DENSITY, TURBO, PRIORITY, FAST_DELIVERY, ECONOMIC), description, deliveredBy (IFOOD, MERCHANT), pickupCode, deliveryDateTime, observations, deliveryAddress | |
| delivery.deliveryAddress | streetName, streetNumber, formattedAddress, neighborhood, complement, reference, postalCode, city, state, country, coordinates.latitude, coordinates.longitude | `postalCode` opcional, pode vir zerado |
| takeout | mode (DEFAULT, PICKUP_AREA), takeoutDateTime, observations | |
| dineIn | deliveryDateTime | |
| schedule | deliveryDateTimeStart, deliveryDateTimeEnd | Pedido agendado |
| picking | picker, replacementOptions | Só grocery |
| additionalInfo.metadata | map string para string | Usado em pedidos POS |

Tratamento de patrocínio dos `benefits[].sponsorshipValues.name` (U/order/details):

| Patrocinador | Tratamento |
|---|---|
| IFOOD | Tratar como pagamento. O iFood repassa o valor à loja |
| EXTERNAL | Tratar como pagamento. O iFood repassa o valor do parceiro externo |
| MERCHANT | Tratar como desconto. Subsídio da loja |
| CHAIN | Tratar como pagamento. Responsabilidade da rede, cobrada na conciliação |

Se o benefício for maior que o total do pedido, o valor a cobrar fica zerado.

## 5. Limites, erros e boas práticas

| Item | Valor | URL |
|---|---|---|
| Polling | 6000 RPM por token (429) | U/events/polling-overview |
| Ack | Até 2000 IDs por chamada (texto e homologação). Ver divergência abaixo | U/events/polling-overview |
| Rastreio | 1 requisição a cada 30 s | U/order/endpoints |
| Detalhes do pedido | Mantidos 7 dias. Não consultar pedidos antigos repetidamente | U/order/workflow |
| Retry de 404 | Backoff exponencial até 10 min. Sem retry infinito | U/order/workflow |
| Erros vistos | 400, 401, 403 (`unauthorizedMerchants`), 404, 413, 422, 429, 500. Corpo `{"code","message"}`, às vezes com `details.reason` | U/order/cancellation ; U/events/polling-overview ; U/order/workflow |
| Idempotência | Confirmação duplicada é ignorada. Eventos duplicados exigem dedupe por `id` | U/order/workflow |
| Versão e base | Order v1.0, Events v1.0, Authentication v1.0 em merchant-api.ifood.com.br | https://developer.ifood.com.br/pt-BR/docs/references |
| Rate limit por endpoint (geral) | [NÃO CONFIRMADO]. Página "Rate limit" de Sobre nossas APIs existe no menu, mas o slug não foi localizado | |
| Página "Erros e troubleshooting" do Order | [NÃO CONFIRMADO]. Slug não localizado | |

## 6. Requisitos de homologação para pedidos

Fonte: U/order/homologation, salvo indicação.

| Requisito | Detalhe |
|---|---|
| Consumo de eventos | Polling com intervalo mínimo de 30 s, ou webhook HTTPS |
| Ack | Confirmar todos os eventos via `/acknowledgment` |
| Confirmação | Em até 8 min para DELIVERY e TAKEOUT, imediato ou agendado |
| Cancelamento | Consultar `/cancellationReasons`, exibir os motivos ao usuário e processar cancelamentos do cliente ou da plataforma |
| Pagamento | Exibir a bandeira do cartão e calcular o troco em dinheiro |
| Cupons | Exibir o valor e quem arca com o custo (iFood ou loja) |
| Observações | Exibir observações de itens, instruções especiais e código de coleta |
| Documento | Exibir CPF ou CNPJ quando fornecido para a NF-e |
| Deduplicação | Detectar e descartar eventos duplicados |
| Sincronização | Atualizar o estado interno se outro sistema alterar o status do pedido |
| Handshake | Processar corretamente os eventos da Plataforma de Negociação |
| Pronto e despacho | `readyToPickup` para TAKEOUT, DINE_IN e DELIVERY. Em entrega própria, `dispatch` após o `readyToPickup` |
| Restaurantes | Exibir observações de entrega na comanda |
| Token e rate limit | Renovar só quando expirado e respeitar os limites |
| Privacidade | Não imprimir CPF e endereço em documentos para parceiros de entrega (U/order/details) |
| Webhook (se usado) | Validar a assinatura, rejeitar assinatura inválida e responder em até 5 s (U/events/webhook-request) |
| Grocery | Ack com até 2000 IDs, importar via virtual-bag só após RFI, `dispatch` só após o fim da separação. Fora do escopo de restaurante |

## 7. Shipping, Logistics e Sob Demanda (provável fora de escopo)

Fonte: https://developer.ifood.com.br/pt-BR/docs/references. A descrição funcional de "Sob Demanda" é [NÃO CONFIRMADO], porque a FAQ só a lista como categoria.

| Módulo | O que a referência lista | Impacto para o EasyStok |
|---|---|---|
| Shipping | Disponibilidade de entrega (delivery availabilities), solicitar entregador (request driver), mudança de endereço (accept, deny, request, userConfirmAddress), cancelamento (reasons, cancel, cancel request driver), safe delivery | Serve para contratar entrega iFood para pedidos de outros canais. Fora de escopo se o EasyStok só recebe pedidos |
| Logistics | getLogisticsOrderDetails, assignDriver, goingToOrigin, arrivedAtOrigin, Dispatched, arrivedAtDestination | Para integradoras logísticas. Estas usam `excludeHeartbeat=true` no polling (U/events/polling-overview). Fora de escopo |

## Divergências internas da doc

| Tema | Versão A | Versão B |
|---|---|---|
| Caminho do polling | `/events/v1.0/events:polling` (referência e U/events/polling-overview) | `/order/v1.0/orders:polling` (U/order/workflow e U/order/endpoints) |
| Caminho do ack | `/events/v1.0/events/acknowledgment`, corpo `[{"id"}]` | `/order/v1.0/orders:acknowledgment`, corpo `{"acknowledgedEventIds":[...]}` |
| Limite do ack | 2000 IDs por chamada (texto de polling e homologação) | 10000 (resposta 413 em U/events/polling-overview) |
| Verbo de readyToPickup e dispatch | POST (U/order/endpoints e U/order/workflow) | PUT (U/order/homologation) |
| Timeout do webhook | 5 s (U/events/webhook-overview) | 30 s (U/order/homologation) |
| Formato de `code` e `fullCode` | PLC/PLACED, CFM/CONFIRMED (U/order/events) | `code` CONFIRMED e `fullCode` ORDER_CONFIRMED (exemplo em U/order/workflow) |
| Estados nos eventos principais | PLC, CFM, RTP etc. (U/order/events) | "CONFIRMED" como "pedido chegou na fila" (U/order/fundamentals) |
| Resposta do detalhe | Campos de U/order/details (sem `status`) | Exemplo com `status` e `scheduling` (U/order/endpoints) |
| Tipo de consumo no local | `DINE_IN` (doc oficial) | `INDOOR` (usado no briefing, sem uso na doc) |

## Lacunas e pontos [NÃO CONFIRMADO]

1. Custo de uso das APIs.
2. Escopos finos por módulo.
3. Rate limit por endpoint (fora do polling e do tracking).
4. Página de erros e troubleshooting do Order.
5. Pedidos agendados, pedidos para retirar e guia de negociação (`handshake-platform-guide`).
6. Descrição funcional de Sob Demanda e Shipping.
7. Changelog e versão da documentação.
8. Qual caminho de polling e ack é o correto (ver divergências). Usar a referência e confirmar em teste real na loja de teste.
9. Registro e IPs do webhook (`webhook-best-practices`, `presence`).
10. Se `/orders/{id}` devolve `status` (divergência entre U/order/details e U/order/endpoints).
11. Prazo de retenção de 8 h só foi lido para o polling. Ele não foi cruzado com o webhook.

## Lista de URLs consultadas

| Status | URL |
|---|---|
| Lida com sucesso (navegador embutido) | https://developer.ifood.com.br/pt-BR |
| Lida | https://developer.ifood.com.br/pt-BR/docs/references |
| Lida | https://developer.ifood.com.br/pt-BR/docs/getting-started |
| Lida | https://developer.ifood.com.br/pt-BR/docs/food/categories |
| Lida | G/first-steps/create-account |
| Lida | G/first-steps/create-app |
| Lida | G/first-steps/request-access |
| Lida | G/first-steps/generate-test-order |
| Lida | U/order/fundamentals |
| Lida | U/order/workflow |
| Lida | U/order/details |
| Lida | U/order/events |
| Lida | U/order/cancellation |
| Lida | U/order/endpoints |
| Lida | U/order/homologation |
| Lida | U/order/handshake-platform |
| Lida | U/events/polling-overview |
| Lida | U/events/webhook-overview |
| Lida | U/events/webhook-request |
| Lida | U/events/webhook-signature |
| Lida | U/authentication/centralized |
| Lida | U/authentication/distributed |
| Parcial (só o índice de perguntas) | https://developer.ifood.com.br/pt-BR/support/faq |
| Erro (404 ou vazio) | WebFetch em todas as URLs de developer.ifood.com.br |
| Erro (404 ou vazio) | https://developer.ifood.com.br/pt-BR/docs/guides/... e .../docs/ |
| Erro (404 ou vazio) | U/order/errors e U/order/troubleshooting |
| Erro (404 ou vazio) | G/homologation/request-access e G/homologation/policy |
| Erro (404 ou vazio) | G/about-apis/rate-limit e G/first-steps/integration-flow |
| Não lidas (existem no menu) | U/order/scheduled-orders, U/order/handshake-platform-guide, U/events/presence, U/events/webhook-best-practices |
