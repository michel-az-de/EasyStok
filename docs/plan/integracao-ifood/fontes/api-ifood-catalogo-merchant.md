# API iFood: catálogo, preço, disponibilidade e status da loja (Merchant + Catalog)

Data de acesso: 2026-11-16. Fonte: portal oficial https://developer.ifood.com.br (PT-BR).
Escopo: Merchant e Catalog (restaurante/Food) para o EasyStok. Autenticação, eventos e pedidos estão em outro documento.

## Legenda de citações

O portal é uma SPA em JavaScript: o WebFetch retornou 404 e a leitura foi feita pelo navegador embutido. Cada afirmação cita uma destas fontes:

| Sigla | Fonte | Observação |
|---|---|---|
| OA[spec] | https://developer.ifood.com.br/page-data/pt-BR/docs/references/page-data.json | OpenAPI de todos os módulos (`allSwaggerSpec`, `specId`). Renderizado em https://developer.ifood.com.br/pt-BR/docs/references |
| G-cat/<slug> | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/catalog/<slug> | Guias do Catalog |
| G-mer | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/merchant/workflow | Guia Merchant |
| RL | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/rate-limit | Rate limit |
| HP | https://developer.ifood.com.br/pt-BR/docs/getting-started/homologation/categories | Política de homologação |
| HC | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/catalog/homologation | Homologação do Catalog |

Servidores (OA): Merchant `https://merchant-api.ifood.com.br/merchant/v1.0`; Catalog v2 `https://merchant-api.ifood.com.br/catalog/v2.0/`; Catalog v1 `.../catalog/v1.0/`.

## 1. Mapa dos módulos

Fonte: OA (versões, paths) e https://developer.ifood.com.br/pt-BR/docs/food/categories.

| Módulo | Versão | Função | Escopo EasyStok |
|---|---|---|---|
| Authentication | v1.0 | OAuth (`/oauth/userCode`, `/oauth/token`) | Outro documento |
| Merchant | v1.0 | Lojas, status, interrupções, horários, tempo de preparo | DENTRO |
| Catalog | v2.0 (v1 descontinuada em 2024) | Cardápio, preço, status, estoque, imagem | DENTRO |
| Events | v1.0 | Polling e ack de eventos | Outro documento |
| Order | v1.0 | Pedidos | Outro documento |
| Shipping | v1.0 | Entrega iFood | Fora |
| Logistics | v1.0 | Rastreamento próprio | Fora |
| Review | v1.0 e v2.0 | Avaliações | Fora |
| Financial | v2.0, v2.1, v3.0 | Conciliação, repasses, vendas | Seção 6 |
| Analytics | v1.0 | KPIs de pedidos | Fora |
| Promotion | v1.0 e v2.0 | Promoções (Groceries) | Fora |
| Picking | v1.0 | Separação de pedido (Groceries) | Fora |
| Item | v1.0 | Ingestão Groceries (`/ingestion/{merchantId}`) | Fora |

Módulo de chat/mensagens: [NÃO CONFIRMADO] (ausente do OA e das categorias).
Módulos por categoria de app (HP): PDV = Merchant, Events, Order, Catalog, Review, Shipping, Analytics. Catálogo = Merchant, Catalog. Groceries = Merchant, Events, Order, Catalog, Item, Picking, Shipping, Financial, Analytics.

## 2. Merchant (status da loja)

Fonte: OA[merchant] e G-mer.

### 2.1 Endpoints

| Endpoint | Método | Finalidade | Fonte |
|---|---|---|---|
| /merchants | GET | Lista lojas (`page` a partir de 1, `size` padrão 100) | OA[merchant]; G-mer |
| /merchants/{merchantId} | GET | Detalhes da loja | OA[merchant]; G-mer |
| /merchants/{merchantId}/status | GET | Disponibilidade de todas as operações | OA[merchant]; G-mer |
| /merchants/{merchantId}/status/{operation} | GET | Status de uma operação (ex.: `delivery`) | OA[merchant] |
| /merchants/{merchantId}/interruptions | GET | Lista interrupções atuais e futuras | OA[merchant] |
| /merchants/{merchantId}/interruptions | POST | Cria interrupção (201) | OA[merchant] |
| /merchants/{merchantId}/interruptions/{interruptionId} | DELETE | Remove (204; 409 se recém-criada) | OA[merchant] |
| /merchants/{merchantId}/opening-hours | GET | Lê horários (404 se não houver) | OA[merchant] |
| /merchants/{merchantId}/opening-hours | PUT | Grava a grade semanal (201) | OA[merchant] |
| /merchants/{merchantId}/myPreparationTime | GET, POST, PUT, DELETE | Tempo de preparo (header `X-iFood-Customer-ID`; inteiro 5 a 70) | OA[merchant], OA[logistics-time-manager]; G-mer |
| /merchants/checkin-qrcode | POST | QR de check-in (só Groceries) | OA[merchant]; G-mer |

### 2.2 Campos

Loja (`Merchant`, OA[merchant]):

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| id | uuid | n/d | Id da loja |
| name, corporateName | string | n/d | Nome público e razão social |
| type | enum RESTAURANT, STORE, GROUP | n/d | Tipo |
| status | enum AVAILABLE, UNAVAILABLE, DISABLED | n/d | Status |
| operations.name | enum delivery, indoor | n/d | Operação |
| operations.salesChannel | objeto (`name` `ifood-app`, `enabled`) | n/d | Canal |
| address, createdAt, description, averageTicket, exclusive | vários | n/d | Demais |

Status (`Status`, OA[merchant]): `operation`, `salesChannel`, `available` (bool), `state` (OK, WARNING, CLOSED, ERROR), `reopenable` (`identifier`, `type` UNAVAILABILITY, `reopenable`), `validations[]` (`id`, `code`, `state`, `message`), `message` (`title`, `subtitle`, `description`).
Códigos de validação (G-mer): `is-connected`, `opening-hours`, `unavailabilities`, `radius-restriction`, `payout-blocked`, `terms-service-violation`.
Regras (G-mer): a loja recebe pedido com horário, catálogo ativo, área de entrega, sem interrupção e com polling a cada 30 s. `reopenable: true` permite remover a pausa pela API; `false` indica penalidade automática.

Interrupção (POST, OA[merchant]; G-mer):

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| description | string | sim | Motivo (guia: até 255 caracteres) |
| start | date-time ISO 8601 | sim | Início |
| end | date-time ISO 8601 | sim | Fim |
| id | string | não | Opcional |

Duração de 1 minuto a 7 dias; sem sobreposição (G-mer).

Horários (PUT, OA[merchant]; G-mer):

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| storeId | uuid | sim | Loja |
| shifts[].dayOfWeek | MONDAY a SUNDAY | sim (G-mer) | Dia |
| shifts[].start | HH:MM:SS | sim (G-mer) | Abertura |
| shifts[].duration | inteiro, minutos | sim (G-mer) | Duração |

Dias não enviados ficam fechados; vários turnos por dia sem sobreposição; a API cobre só o horário padrão do Marketplace (catálogo digital e horários especiais ficam no Portal do Parceiro) (G-mer).

## 3. Catalog v2.0

Fonte: OA[catalog-v2]; conceitos em G-cat/{introduction, workflow, fundamentals, errors-and-troubleshooting, versioning, guides/availability, guides/multisetup, guides/common-patterns}.

### 3.1 Estrutura

Catálogo > Contexto > Categoria > Item > produtos e grupos de complementos > opções; cada nível tem status próprio (G-cat/fundamentals).

| Contexto | Canal | Fonte |
|---|---|---|
| DEFAULT | Entrega | G-cat/fundamentals |
| WHITELABEL | Cardápio Digital | G-cat/fundamentals |
| INDOOR | Consumo no Local | G-cat/fundamentals |

- Item `type`: DEFAULT, PIZZA, COMBO_V2 (OA). Categoria `template`: DEFAULT, PIZZA; no máximo uma categoria PIZZA por loja (G-cat/fundamentals).
- Tipos de grupo: OFFER_UNIT, SPECIFICATION, INGREDIENTS, CUTLERY; pizza usa SIZE, CRUST, EDGE, TOPPING (G-cat/fundamentals).

### 3.2 Endpoints (base /merchants/{merchantId}; fonte OA[catalog-v2])

| Endpoint | Método | Finalidade |
|---|---|---|
| /catalogs | GET | Lista catálogos (`catalogId`, `status`, `context`, `modifiedAt`, `groupId`) |
| /catalogs/{catalogId}/unsellableItems | GET | Itens não vendáveis |
| /catalogs/{groupId}/sellableItems | GET | Itens vendáveis |
| /catalogs/{catalogId}/categories | GET, POST | Lista (`includeItems`) e cria categoria |
| /catalogs/{catalogId}/categories/{categoryId} | GET, PATCH | Lê e edita categoria |
| /categories/{categoryId} | DELETE | Exclui categoria |
| /categories/{categoryId}/items | GET | Itens da categoria |
| /categories/{categoryId}/products/{productId} | DELETE | Exclui item (`catalogContext`) |
| /items | PUT | Cria ou substitui item e entidades ligadas (idempotente) |
| /items/{itemId} | PATCH | Merge-patch RFC 7386: `status`, `price`, `externalCode`, `index`, `shifts` |
| /items/{itemId}/flat | GET | Item achatado |
| /items/price, /items/status, /items/externalCode | PATCH | Marcados como obsoletos (usar PATCH /items/{itemId}) |
| /products | GET (`limit`, `page` obrigatórias), POST | Lista e cria produto |
| /products/{productId} | PUT, PATCH, DELETE | Edita, merge-patch e exclui |
| /product/{productId} | GET | Produto por id |
| /products/externalCode/{externalCode} | GET | Produtos por código externo |
| /products/price | PATCH | Preço em lote, assíncrono (202 `batchId`) |
| /products/status | PATCH | Status em lote (AVAILABLE, UNAVAILABLE, DELETED), assíncrono (202) |
| /batch/{batchId} | GET | Resultado do lote (`batchStatus` COMPLETED, PROCESSING, NONEXISTENT_BATCH, WRONG_BATCH_SIZE; `results[]` com `resourceId`, `result` SUCCESS/FAILURE, `failureReason`) |
| /optionGroups | GET | Lista grupos (`includeOptions`, `catalogContext`) |
| /optionGroups/{id} | PATCH, DELETE | Edita e exclui grupo |
| /optionGroups/{id}/status | PATCH | Status do grupo |
| /optionGroups/{id}/products/{productId} | DELETE | Desassocia grupo |
| /optionGroups/{id}/options | POST | Cria opção |
| /optionGroups/{id}/products/{productId}/option | DELETE | Exclui opção |
| /options/price, /options/status, /options/externalCode | PATCH | Atualiza opção |
| /inventory | POST | Cria ou atualiza estoque |
| /inventory/{productId} | GET | Lê estoque |
| /inventory/batchFetch, /inventory/batchDelete | POST | Lote de estoque |
| /image/upload | POST | Upload (`{"image":"data:image/png;base64,..."}`) |
| /catalog/version | GET | Versão do catálogo da loja |
| /version/upgrade, /version/downgrade | POST | Migra entre v1 e v2 |

### 3.3 Campos principais (OA[catalog-v2])

Item (`ItemDto`):

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| id | string | não | UUID v4 gerado pelo integrador (G-cat/introduction) |
| type | enum | sim | DEFAULT, PIZZA, COMBO_V2 |
| categoryId | string | não | Categoria |
| status | enum | sim | AVAILABLE, UNAVAILABLE |
| price | `{value, originalValue}` | não | `originalValue` > `value` se enviado |
| externalCode | string | sim | Código PDV; o do item prevalece sobre o do produto |
| index | number | não | Ordenação |
| productId | string | sim | Produto |
| shifts | array | não | Janelas de venda |
| contextModifiers | array | não | `catalogContext` (obrig.), `status`, `price`, `externalCode`, `itemContextId` |

`PUT /items` (`FullItemDto`): `item` obrigatório, mais `products[]`, `optionGroups[]`, `options[]`; o guia manda enviar os quatro sempre (G-cat/introduction).

Produto:

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| id | string | não | UUID |
| name | string | sim | 1 a 150 |
| description | string | não | Até 2000 |
| externalCode | string | não | Até 150 |
| additionalInformation | string | não | Até 200 |
| imagePath | string | não | Até 120; vem do upload |
| image / multipleImages | base64 | não (POST) | Imagens |
| ean | string | não | 13 caracteres |
| serving | enum | sim | NOT_APPLICABLE, SERVES_1 a SERVES_4 |
| dietaryRestrictions | array enum | não | VEGETARIAN, VEGAN, ORGANIC, GLUTEN_FREE, SUGAR_FREE, LAC_FREE, ALCOHOLIC_DRINK, NATURAL, ZERO, DIET |
| weight | `{quantity, unit kg/g}` | não | Informativo |
| optionGroups | array `{id, min, max, index}` | não | Associação |
| industrialized | boolean | leitura | Se verdadeiro, não editável |

Grupo de opção: `id`, `name` (1 a 150), `status`, `externalCode` (até 100), `index`, `min`, `max`, `optionGroupType` obrigatórios; `optionIds[]` opcional. Opção: `id`, `status`, `index`, `productId`, `price`, `fractions`, `externalCode`, `contextModifiers`. Categoria: `name` (até 100), `status`, `template` obrigatórios; `externalCode`, `index`, `id` opcionais.
Estoque (`PostInventoryItemDTO`): `productId` obrigatório, `amount` number. Resposta: `ownerId`, `productId`, `amount`, `globalStock`, `allowNegativeStock`, `inStock`. Quando `amount` chega a zero, itens ligados são pausados.
Lote de preço: `productId` ou `externalCode`, `price` obrigatório, `resources[]` (ITEM, OPTION), `catalogId`. Lote de status: idem com `status` obrigatório. Resposta 202 `{url, batchId}`.

### 3.4 externalCode (vínculo com o ERP)

- String livre do integrador, única por merchant, recomendada para sincronização (G-cat/introduction, G-cat/fundamentals).
- Aceito nos lotes no lugar de `productId` e consultável em `GET /products/externalCode/{externalCode}` (OA).
- `id` deve ser UUID v4; outro formato retorna 404 (G-cat/introduction).
- Duplicidade: comportamento inconsistente (seção 8).
- Inferência (não da doc): o EasyStok guarda o par `id` UUID v4 + `externalCode` por produto e contexto.

### 3.5 Contextos, disponibilidade e estoque

- Preço e status por canal via `contextModifiers` (G-cat/guides/multisetup, availability); contextos não listados herdam a raiz.
- Pausar item: `PATCH /items/{itemId}`; opção: `PATCH /options/status`; grupo: `PATCH /optionGroups/{id}/status` (OA; G-cat/guides/availability).
- Pausa automática por estoque só com inventário configurado (G-cat/guides/availability).
- Item some do app se: item ou categoria não AVAILABLE, sem `price.value`, grupo obrigatório sem opção AVAILABLE, turno fora do horário ou contexto errado (G-cat/errors-and-troubleshooting).

### 3.6 Restrições

| Item | Valor | Fonte |
|---|---|---|
| Nome do produto | 1 a 150 caracteres | OA[catalog-v2] |
| Descrição do produto | até 2000 | OA[catalog-v2] |
| Nome da categoria | até 100 | OA[catalog-v2] |
| Imagem | jpg, jpeg, png, menor que 5 MB (413 acima) | G-cat/errors-and-troubleshooting |
| Homologação | título até 100, descrição até 500 | HC |
| Pizza | `type` PIZZA, sem `categoryId`; grupos SIZE, CRUST, EDGE, TOPPING com opção com preço | G-cat/errors-and-troubleshooting |
| 422 | item sem preço, `min` > `max`, mais de uma categoria PIZZA | G-cat/errors-and-troubleshooting |

### 3.7 Lote e propagação

- Lotes são assíncronos: consultar `GET /batch/{batchId}` a cada 5 a 10 s até COMPLETED e conferir cada `result` (G-cat/errors-and-troubleshooting).
- SLA oficial de propagação até o app: [NÃO CONFIRMADO]. O 2 s e o 10 s de HC são critérios da aplicação, não SLA do iFood.
- G-cat/introduction diz que o item "aparece imediatamente" após o PUT, sem número.
- Janelas técnicas do Catalog (4 h, 01:00 às 05:00) anunciadas em 03/11/2026 na home https://developer.ifood.com.br/pt-BR/docs/getting-started; cronograma não lido.

### 3.8 Catalog x Item

- Catalog: cardápio de restaurante (Food).
- Item v1.0: produtos de Groceries (`POST/PATCH /ingestion/{merchantId}`, OA[item]); não se aplica a restaurante.
- v2.0 ativa; v1 descontinuada em 2024 (G-cat/versioning); migração por `POST /version/upgrade` (OA).

## 4. Limites, erros, versão e URL base

Rate limit (RL), excesso retorna 429:

| Grupo | Limite |
|---|---|
| Catalog v2: categorias, catálogos, produtos, item PUT, optionGroups DELETE, version | 3000 req/min |
| Catalog v2: `/products/status`, `/products/price`, optionGroups (GET, PATCH, status), options, `/batch`, `/inventory` | 5000 req/min |
| Catalog v1 | 15000 req/min |
| Merchant GET /merchants | 100 req/min |
| Merchant GET /merchants/{id} | 300 req/min |
| Merchant GET status | 20000 req/min |
| Merchant interruptions GET 100, POST 200, DELETE 100; opening-hours GET 100, PUT 100 | req/min |
| Item (ingestion) | 1000 req/min |

RL cita `/item/price`, `/item/status` e changelog, que não existem com esse nome no OA v2. Sem limite listado: `PATCH /items/{itemId}`, `/image/upload`, `/inventory/batchFetch`, `PATCH /products/{id}`, `myPreparationTime` [NÃO CONFIRMADO].

| Tema | Conteúdo | Fonte |
|---|---|---|
| Erros Catalog | 400, 401, 403, 404, 409, 413, 422, 429, 500; backoff imediata, 1 s, 2 s, 4 s; não repetir 4xx exceto 429 | G-cat/errors-and-troubleshooting |
| Corpo de erro | `code`, `message`, `requestId`, `details[]` (`code`, `field`, `message`) | OA[catalog-v2] |
| Erros Merchant | 400, 401, 403, 404, 409, 500 | OA[merchant] |
| Uso indevido | Erro máx. 1% em `/status`, 2% em `/merchants`, `/merchants/{id}` e `/interruptions` (24 h); exceder rate limit; app sem chamadas por 90 dias; bloqueio em 3 níveis | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/improper-use |
| Boas práticas | Retry em 5xx; processar cada merchant isolado; tolerar enums novos; reusar token | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/best-practices |
| Rede | HTTPS 443 para `merchant-api.ifood.com.br`; algumas hospedagens recebem 403 da CDN | https://developer.ifood.com.br/pt-BR/docs/getting-started/first-steps/create-app |

## 5. Homologação

Política (HP): Catalog e Merchant por chamado; Order e Events (Food) automáticos; Financial em chamado separado; Food e Groceries não compartilham chamado; app somente leitura não é aceito nos módulos operacionais (exceto Financial e Analytics). Pré-requisitos: desenvolvimento completo, conta Profissional (CNPJ), aplicação funcional completa.

Catalog (HC): testa a aplicação, não curl. Critérios: categorias, item simples (`PUT /items`), listagens, complementos com `min`/`max`, pizza e combo (se aplicável), lote de preço e status, `contextModifiers`, agendamento, multi-catálogo (se aplicável), validações (título até 100, descrição até 500, preço positivo), erros, retry com backoff sem repetir 4xx, preço e status refletidos em até 2 s, 100+ itens em até 10 s, timeout de 30 s. Processo: abrir ticket, receber checklist, gravar vídeos com data e hora, resposta em até 5 dias úteis.
Merchant: sem página própria de critérios localizada; homologado por chamado (HP). Critérios específicos: [NÃO CONFIRMADO].

## 6. Financial (resumo)

Fonte: OA[financial-v3], OA[financial-v21], OA[financial-v2]; chamado separado (HP).

- v3.0: `GET /merchants/{id}/settlements`, `/anticipations`, `/sales`, `/financial-events`; `POST /reconciliation/on-demand` e `GET /reconciliation/on-demand/{requestId}`.
- v2.0 (12 rotas): salesAdjustments, payments, paymentDetails, occurrences, maintenanceFees, incomeTaxes, periods, chargeCancellations, cancellations, receivableRecords, salesBenefits, adjustmentsBenefits.
- v2.1: `GET /sales`.
- Limite 1000 req/min por endpoint (RL).
- Inferência: fora da primeira entrega; avaliar depois para conciliação.

## 7. Termos, tipos de app e autorização da loja

Fonte: https://developer.ifood.com.br/pt-BR/docs/getting-started/first-steps/create-app e https://developer.ifood.com.br/pt-BR/docs/getting-started/first-steps/request-access.

| Tema | Conteúdo |
|---|---|
| Centralizado | SaaS, uma instância com acesso a várias lojas; a loja aprova na aba Permissões (busca por ID ou CNPJ) no Portal do Parceiro; gerar novo accessToken |
| Distribuído | On-premises, acesso restrito à loja; `userCode` em `authentication/v1.0/oauth/userCode`, o usuário autoriza e recebe `authorizationCode`, trocado em `oauth/token` |
| Visibilidade | Público (marketplace do Portal do Parceiro) ou privado |
| Teste | Cadastro cria app e loja de teste; produção só após homologar |
| Acessos | Solicitar acessos exige homologação concluída |
| Termos | https://developer.ifood.com.br/pt-BR/terms-of-use/developer não lido; cláusulas [NÃO CONFIRMADO] |

Inferência: para o EasyStok (SaaS multi-loja) o modelo centralizado parece aderente; validar com a frente de autenticação.

## 8. Divergências guia x OpenAPI

| # | Tema | Guia | OpenAPI | Fontes | Tratamento |
|---|---|---|---|---|---|
| 1 | Criar categoria | `POST /categories` | `POST /catalogs/{catalogId}/categories` | G-cat/introduction; OA[catalog-v2] | Seguir OA; validar em sandbox |
| 2 | Preço em lote | `PATCH /items/price`, corpo `{"prices":[...]}` | `PATCH /products/price`, array; `/items/price` obsoleto | G-cat/workflow, common-patterns; OA | Seguir OA |
| 3 | Status em lote | `PATCH /items/status`, `{"items":[...]}` | `PATCH /products/status`; unitário por `PATCH /items/{itemId}` | G-cat/guides/availability; OA | Seguir OA |
| 4 | Corpo do estoque | `quantity` | `amount` | G-cat/guides/availability; OA | Seguir OA |
| 5 | `externalCode` duplicado | 409 em algumas páginas, reuso do registro em outras | Sem descrição de unicidade | G-cat/introduction, fundamentals, errors-and-troubleshooting | Testar em sandbox |
| 6 | Pizza, grupo EDGE | Obrigatório (fundamentals) | Opcional (versioning) | G-cat/fundamentals, versioning | Fora do escopo inicial |
| 7 | `modifiedAt` | Numérico (1640000000.12345) | string date-time | G-cat/endpoints; OA GetCatalogDto | Parser tolerante |
| 8 | Multi-menu | `PUT /multisetup/items` | `/multisetup/*` só no OA do Catalog v1 | G-cat/workflow; OA[catalog-v1], OA[catalog-v2] | [NÃO CONFIRMADO]; usar `contextModifiers` |
| 9 | Obrigatórios do item | `externalCode` "opcional, recomendado" | `externalCode` e `productId` obrigatórios | G-cat/introduction; OA | Enviar sempre |
| 10 | Rate limit | `/item/price`, `/item/status`, changelog | Inexistentes no v2 | RL; OA | Limites aproximados |
| 11 | `min`/`max` do grupo | Alguns exemplos omitem | Obrigatórios | G-cat/introduction, workflow; OA | Enviar sempre |
| 12 | Horários (GET) | `id`, `dayOfWeek`, `start`, `duration` | `shifts: array<object>` sem detalhe | G-mer; OA[merchant] | Confirmar em sandbox |

## 9. Lacunas e pontos [NÃO CONFIRMADO]

- SLA de propagação de preço, status e catálogo até o app.
- Valores aceitos de `{operation}` em `GET /merchants/{id}/status/{operation}` (só `delivery` como exemplo).
- Rate limit de `PATCH /items/{itemId}`, `/image/upload`, `/inventory/batchFetch`, `PATCH /products/{id}`, `myPreparationTime`.
- Tamanho máximo de lote (413 sem número, G-cat/errors-and-troubleshooting).
- Comportamento real de `externalCode` duplicado (409 ou reuso).
- `PUT /multisetup/items` no v2.
- Critérios de homologação do Merchant.
- Guia `.../catalog/guides/modifiers-management` e changelog retornaram vazios; cronograma das janelas técnicas (03/11/2026) não lido.
- Item e Financial: só OpenAPI lido, sem guias; termos de uso não lidos; guias de pizza e combo não lidos.
- Chat/mensagens: não encontrado.
- Se o iFood decrementa `amount` por pedido: [NÃO CONFIRMADO]; `globalStock` sem detalhe.
- Descrições do OpenAPI vêm em inglês e podem estar defasadas em relação aos guias.

## 10. URLs consultadas (prefixo https://developer.ifood.com.br)

| URL | Status |
|---|---|
| /pt-BR/docs/guides (WebFetch) | 404 |
| /pt-BR/docs/guides/catalog/overview (WebFetch) | 404 |
| WebSearch `site:developer.ifood.com.br catalog v2` | Sem resultado do domínio |
| /pt-BR/docs/getting-started | Lido |
| /pt-BR/docs/food/categories | Lido |
| /pt-BR/docs/references | Lido |
| /page-data/pt-BR/docs/references/page-data.json | Lido (19 specs) |
| /pt-BR/docs/food/guides/modules/catalog/introduction | Lido |
| .../catalog/workflow | Lido |
| .../catalog/fundamentals | Lido |
| .../catalog/endpoints | Lido (parcial) |
| .../catalog/errors-and-troubleshooting | Lido |
| .../catalog/versioning | Lido |
| .../catalog/homologation | Lido |
| .../catalog/guides/common-patterns | Lido |
| .../catalog/guides/availability | Lido |
| .../catalog/guides/multisetup | Lido |
| .../catalog/guides/modifiers-management | Vazio |
| .../catalog/guides/pizza, .../guides/combo | Existem; não lidos |
| /pt-BR/docs/food/guides/modules/merchant/workflow | Lido |
| /pt-BR/docs/getting-started/documentation/rate-limit | Lido |
| .../documentation/best-practices | Lido |
| .../documentation/improper-use | Lido |
| /pt-BR/docs/getting-started/homologation/categories | Lido |
| .../homologation/overview | Vazio |
| /pt-BR/docs/getting-started/first-steps/create-app | Lido |
| .../first-steps/request-access | Lido |
| .../first-steps/integration-flow | Vazio |
| /pt-BR/docs/changelog | Vazio |
| .../modules/item/introduction, .../item/homologation, .../financial/homologation | Existem; não lidos |
| /pt-BR/terms-of-use/developer | Link visto; não lido |
