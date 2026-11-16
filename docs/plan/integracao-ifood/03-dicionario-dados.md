# 3. Dicionário de dados das entidades do iFood envolvidas

Nível de entidade: identificador, campos que importam ao EasyStok, ciclo de vida. A lista campo a campo, com tipos,
está nas fontes: [pedido §4.4](fontes/api-ifood-pedidos.md) e [catálogo §2.2 e §3.3](fontes/api-ifood-catalogo-merchant.md).
Siglas de URL no [README](README.md). A doc do pedido não marca obrigatoriedade campo a campo (U/order/details).

## 3.1 Entidades

| Entidade | Identificador | Campos relevantes | Ciclo de vida | Fonte |
|---|---|---|---|---|
| Token | — | `accessToken`, `type`, `expiresIn` (21600) | Expira em 6 h | U/authentication/distributed |
| Evento | `id` | `code`, `fullCode`, `orderId`, `merchantId`, `salesChannel`, `createdAt` (UTC), `category`, `metadata` | Retido 8 h; exige ack único | U/order/events; U/events/polling-overview |
| Pedido | `id` (uuid); `displayId` amigável | `orderType`, `orderTiming`, `salesChannel`, `category`, `createdAt`, `preparationStartDateTime`, `isTest`, `extraInfo`, `merchant`, `customer`, `items`, `benefits`, `total`, `additionalFees`, `payments`, `delivery`, `takeout`, `dineIn`, `schedule` | PLACED a CONCLUDED ou CANCELLED; detalhe consultável 7 dias | U/order/details; U/order/workflow |
| Cliente do pedido | `customer.id` | `name`, `documentNumber` e `documentType` (opcionais, para NF), `phone.number` e `phone.localizer` (somem 3 h após a entrega), `ordersCountOnMerchant` | Dado do iFood; segmentação é confidencial | U/order/details |
| Item do pedido | `index`, `id`, `uniqueId` | `externalCode` (opcional), `name`, `quantity`, `unit` (UN, g, kg, ml, L), `unitPrice`, `optionsPrice`, `totalPrice`, `observations` (até 1024) | Pode mudar por OPA | U/order/details |
| Complemento (2º nível) | `index`, `id` | `name`, `groupName`, `externalCode`, `quantity`, `unitPrice`, `addition`, `price` | | U/order/details |
| Customização (3º nível) | `id` | igual ao complemento | | U/order/details |
| Benefício | `targetId` | `value`, `target` (CART, DELIVERY_FEE, ITEM, PROGRESSIVE_DISCOUNT_ITEM), `sponsorshipValues[]` (IFOOD, MERCHANT, EXTERNAL, CHAIN; a mesma página traz OWN e PARTNER, ver 02 §2.8), `campaign` | | U/order/details |
| Total | — | `subTotal`, `deliveryFee`, `benefits`, `additionalFees`, `orderAmount` = subTotal + deliveryFee + additionalFees − benefits | | U/order/details |
| Taxa adicional | `type` (SMALL_ORDER_FEE) | `value`, `liabilities` | Receita do iFood, fora da NF | U/order/details |
| Pagamento | — | `prepaid` (online), `pending` (cobrar na entrega), `methods[]`: `method` (CASH, CREDIT, DEBIT, MEAL_VOUCHER, FOOD_VOUCHER, GIFT_CARD, DIGITAL_WALLET, PIX, OTHER), `type` (ONLINE, OFFLINE), `card.brand`, `cash.changeFor` | | U/order/details |
| Entrega | — | `deliveredBy` (IFOOD, MERCHANT), `mode`, `pickupCode`, `deliveryDateTime`, `observations`, `deliveryAddress` (rua, número, bairro, complemento, referência, CEP opcional, cidade, UF, coordenadas) | | U/order/details |
| Retirada | — | `takeout.mode`, `takeoutDateTime`, `observations` | | U/order/details |
| Agendamento | — | `schedule.deliveryDateTimeStart`, `deliveryDateTimeEnd` | Regras de pedido agendado `[NÃO CONFIRMADO]` | U/order/details |
| Disputa | `disputeId` | `action`, `handshakeType`, `timeoutAction`, `expiresAt`, `alternatives[]`, `acceptCancellationReasons` | HSD abre, HSS fecha (ACCEPTED, REJECTED, EXPIRED, ALTERNATIVE_REPLIED) | U/order/handshake-platform |
| Motivo de cancelamento | `code` (string) | `description` | Lista dinâmica por pedido | U/order/cancellation |
| Loja (merchant) | `id` (uuid) | `name`, `corporateName`, `type`, `status`, `operations` | | OA; G-mer |
| Status da loja | `operation` | `available`, `state`, `reopenable`, `validations[]`, `message` | Recalculado pelo iFood | OA; G-mer |
| Interrupção | `id` | `description`, `start`, `end` | 1 min a 7 dias | OA; G-mer |
| Horário | `storeId` | `shifts[]`: `dayOfWeek`, `start`, `duration` | Grade semanal | OA; G-mer |
| Catálogo | `catalogId` | `context` (DEFAULT, WHITELABEL, INDOOR), `status`, `modifiedAt`, `groupId` | | OA; G-cat/fundamentals |
| Categoria | `id` | `name` (até 100), `status`, `template` (DEFAULT, PIZZA), `externalCode`, `index` | | OA |
| Item do catálogo | `id` (UUID v4 do integrador) | `type` (DEFAULT, PIZZA, COMBO_V2), `categoryId`, `status`, `price{value, originalValue}`, `externalCode`, `productId`, `shifts`, `contextModifiers` | | OA; G-cat/introduction |
| Produto | `id` | `name` (1 a 150), `description` (até 2000), `externalCode` (até 150), `imagePath`, `ean`, `serving`, `dietaryRestrictions`, `optionGroups[]` (id, min, max) | | OA |
| Grupo de complementos | `id` | `name`, `status`, `externalCode`, `min`, `max`, `optionGroupType` | | OA |
| Opção | `id` | `productId`, `status`, `price`, `externalCode`, `contextModifiers` | | OA |
| Estoque | `productId` | `amount`, `inStock`, `globalStock`, `allowNegativeStock` | Em zero pausa os itens | OA |
| Lote | `batchId` | `batchStatus`, `results[]` (`resourceId`, `result`, `failureReason`) | Assíncrono | OA |

## 3.2 Enumerações que exigem tradução

| Enum iFood | Valores | Fonte |
|---|---|---|
| `orderType` | DELIVERY, TAKEOUT, DINE_IN | U/order/details |
| `orderTiming` | IMMEDIATE, SCHEDULED | U/order/details |
| `salesChannel` | IFOOD, DIGITAL_CATALOG, POS, TOTEM, IFOOD_SHOP, GROCERY_WHITELABEL | U/order/details |
| `delivery.deliveredBy` | IFOOD, MERCHANT | U/order/details |
| `payments.methods[].method` | CASH, CREDIT, DEBIT, MEAL_VOUCHER, FOOD_VOUCHER, GIFT_CARD, DIGITAL_WALLET, PIX, OTHER | U/order/details |
| `sponsorshipValues[].name` | IFOOD e EXTERNAL e CHAIN (tratar como pagamento), MERCHANT (tratar como desconto). `[NÃO CONFIRMADO]`: a página também lista OWN (pagamento) e PARTNER (desconto); provar no spike | U/order/details |
| Status de item e produto | AVAILABLE, UNAVAILABLE (DELETED no lote) | OA |
| Regra geral | Tolerar valores novos de enum | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/best-practices |
