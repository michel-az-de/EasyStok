# R1 - Revisao independente da API iFood

Revisor cetico, sem participacao na producao do estudo. Acesso em 2026-11-16, pelo navegador embutido.
Metodo: `page-data.json` de cada pagina (https://developer.ifood.com.br/page-data/pt-BR/docs/<caminho>/page-data.json) e OpenAPI parseado de https://developer.ifood.com.br/page-data/pt-BR/docs/references/page-data.json (19 specs). Siglas conforme o README do estudo.

| # | Arquivo:secao do estudo | Afirmacao | URL conferida | Veredito | O que a doc diz de fato |
|---|---|---|---|---|---|
| 1 | 02-api 2.4 | Prazo de aceite 8 min; IMMEDIATE de createdAt, SCHEDULED de preparationStartDateTime; sem aceite cancela e penaliza | U/order/fundamentals | CONFIRMADA | Texto identico. A pagina de homologacao Order diz "8 min para DELIVERY e TAKEOUT, independente de orderTiming" (tensao interna da doc) |
| 2 | 02-api 2.3 | Polling GET /events/v1.0/events:polling a cada 30 s; 200 lista, 204 vazio | U/events/polling-overview; OA Events | CONFIRMADA | "every 30 seconds"; 200 com lista, 204 sem eventos |
| 3 | 02-api 2.3 | Ack depois de persistir, para todo evento, inclusive nao usados | U/events/polling-overview | CONFIRMADA | "Send ACK only after ensuring you have stored the event". OMISSAO: eventos SEM filtro recebem auto-ack |
| 4 | 02-api 2.3 | Retencao 8 h | U/events/polling-overview | PARCIAL | "ate 8 horas APOS A ENTREGA do pedido"; so retorna eventos sem ACK. O estudo omite "apos a entrega" |
| 5 | 02-api 2.3 e 2.7 | Polling 6000 req/min | RL; U/events/polling-overview | CONFIRMADA (com ressalva) | RL: GET /events:polling e POST /acknowledgment 6000 req/min. Mas a pagina de polling diz 1 request a cada 30 s por token, e improper-use lista "polling com intervalo menor que 30 s" como infracao. 6000 e teto tecnico, nao meta |
| 6 | 02-api 2.3 | x-polling-merchants ate 100; obrigatorio acima de 500 | U/events/polling-overview | CONFIRMADA | Ate 500 lojas sem header recebe todas; acima de 500 obrigatorio, lotes de 100; erro "Too many polling merchants"; 403 com unauthorizedMerchants |
| 7 | 02-api 2.3 | 50 entregas sem ack = strike; 100 strikes = 5 min de bloqueio + 60 min cooldown | U/events/polling-overview | CONFIRMADA | Idem; strike TTL 30 min renovado; bloqueio responde 429; vigora desde 2 abr 2026 |
| 8 | 02-api 2.8 | Limite do ack 2000 vs 10000 | U/events/polling-overview; OA Events | CONFIRMADA | Pagina e homologacao: 2000; OA: maxItems 10000 (413). Adotar 2000 |
| 9 | 02-api 2.3 | Webhook so centralizado; 202 em 5 s; retentativa 15 min; desabilitado apos 72 h | U/events/webhook-overview; webhook-request | CONFIRMADA | 5 s e 15 min literais; 72 h = erro critico nao resolvido. Webhook NAO tem ack e pode descartar eventos: usar polling como fallback |
| 10 | 02-api 2.3 | HMAC-SHA256, header X-IFood-Signature, clientSecret | U/events/webhook-signature | CONFIRMADA | HMAC SHA256 em hexadecimal, clientSecret, comparacao segura |
| 11 | 02-api 2.8 | Timeout webhook 5 s vs 30 s | webhook-request; order/homologation | CONFIRMADA | Homologacao Order: "respond within 30 seconds"; webhook-request: 5 s |
| 12 | 02-api 2.4 | confirm/startPreparation/readyToPickup/dispatch/requestCancellation; dispatch so entrega propria, apos o pronto | U/order/endpoints; OA Order | CONFIRMADA | OA Order: todos POST; dispatch "only for merchant delivery"; readyToPickup antes de dispatch |
| 13 | 02-api 2.8 | Verbo readyToPickup/dispatch POST vs PUT | order/endpoints; order/homologation | CONFIRMADA | OA e endpoints: POST; homologacao diz PUT. Usar POST |
| 14 | 02-api 2.4 | Conclusao automatica: iFood 6 h; propria 4 h | U/order/workflow (secao 17) | PARCIAL | Tabela: Restaurante+iFood "entrega ou 6 h" (imediato) e "6 h apos o agendamento"; Restaurante+Merchant 4 h / "4 h apos o agendamento"; mercado e farmacia 13 h. Estudo omite a regra do agendado |
| 15 | 02-api 2.4 | Motivos de cancelamento 501 a 512 | U/order/cancellation | PARCIAL | 501-509, 511, 512. NAO existe 510. "Outros codigos reservados ao iFood" |
| 16 | 02-api 2.4 | Disputas HSD/HSS; accept/reject/alternative; OPA | U/order/events; OA Order | CONFIRMADA | HSD abre, HSS resolve, OPA = ORDER_PATCHED. OA: POST /disputes/{id}/accept, /reject, /alternatives/{alternativeId}. Todos os 25 codigos da lista existem na pagina de eventos |
| 17 | 03 3.1 Pagamento | prepaid/pending e cash.changeFor | OA Order (Payments, PaymentMethod, CashInformation) | CONFIRMADA | prepaid ja pago, nao cobrar; pending cobrar na entrega; cash.changeFor "amount to prepare change for" |
| 18 | 03 3.1/3.2 Beneficio | sponsorshipValues; IFOOD/EXTERNAL/CHAIN = pagamento, MERCHANT = desconto | U/order/details; OA Order (Benefit, Sponsorship) | PARCIAL | A pagina de detalhes traz DUAS tabelas contraditorias: (a) IFOOD, EXTERNAL, MERCHANT, CHAIN como o estudo; (b) OWN, EXTERNAL, PARTNER, CHAIN (OWN = pagamento, PARTNER = desconto). A tabela de campos usa "sponsorships.liability" e a OA usa "sponsorshipValues[].name". Registrar como divergencia e provar na loja de teste |
| 19 | 03 3.1 Cliente | telefone some 3 h apos a entrega | U/order/details | CONFIRMADA | "phone e opcional e deixa de ser enviado 3 horas apos a data de entrega". A mesma frase diz "utilize o chat" para contatar o cliente (ver Achados) |
| 20 | 02-api 2.6 | POST /inventory com amount; zero pausa os itens | OA Catalog | CONFIRMADA | `amount`: "When this value reaches zero, the related items will be paused"; chave `productId` |
| 21 | 02-api 2.6 | PUT /items; PATCH /products/price e /products/status em lote; items/price e items/status obsoletos | OA Catalog | CONFIRMADA | OA marca items/price, items/status, items/externalCode como deprecated. Retorna batchId e URL. Atencao: a pagina de homologacao Catalog ainda cita PATCH /items/price e /items/status |
| 22 | 01-prep 1.2 | Homologacao Order/Events: 5 etapas, 1 por 4 h, CNPJ | order/homologation; HP | CONFIRMADA | "5 steps" (conectividade, receber/confirmar com ack, cancelamento, despacho, conclusao), ate 5 capturas por etapa; "uma homologacao a cada 4 horas"; CNPJ obrigatorio, CPF nao aceito; so apps da categoria PDV; Catalog e Review por chamado depois |
| 23 | 01-prep 1.2 | Homologacao Catalog: 2 s e 10 s | HC | CONFIRMADA | "ate 2 segundos" para preco e status; "100+ itens em ate 10 segundos"; tambem exige tratar timeout acima de 30 s |
| 24 | 02-api 2.4 | Detalhe do pedido 404 com retry/backoff ate 10 min; guardado 7 dias | U/order/workflow | CONFIRMADA | "backoff exponencial por ate 10 minutos"; "A API mantem detalhes por apenas 7 dias" |
| 25 | 02-api 2.2 | client_credentials sem refresh token; token 21600 s | U/authentication/centralized; distributed | CONFIRMADA | "Aplicativos centralizados nao recebem refresh tokens"; expiresIn 21600; userCode valido por 10 min (600 s) |
| 26 | 02-api 2.2 | Token de ate 8000 caracteres | U/authentication/distributed | NAO VERIFICAVEL | Nao localizei "8000" nas paginas de autenticacao |
| 27 | 02-api 2.5 | Interrupcao 1 min a 7 dias, sem sobreposicao; tempo de preparo 5 a 70 | G-mer; OA Merchant | CONFIRMADA | "Duracao minima 1 minuto, maxima 7 dias. Pausas nao podem se sobrepor"; myPreparationTime minimum 5, maximum 70 |
| 28 | 02-api 2.5 | Polling mantem a loja online (validacao is-connected) | G-mer | CONFIRMADA | `is-connected` — "Polling every 30 seconds"; "Continue polling to avoid order loss" |
| 29 | 02-api 2.6 | externalCode unico por merchant; id do item UUID v4 do integrador | G-cat/introduction; G-cat/errors-and-troubleshooting | CONFIRMADA | "externalCode es unico por comerciante" (409 se repetir); id UUID v4 obrigatorio, outro formato gera 404; externalCode opcional |
| 30 | 02-api 2.7 | Catalog v2 3000 req/min; 5000 em preco, status, estoque e batch | RL | CONFIRMADA | PATCH /products/status e /price 5000; POST /inventory e GET /batch 5000; demais 3000 |
| 31 | 02-api 2.7 | Merchant 100 a 300 req/min; status 20000 | RL | CONFIRMADA | GET /merchants 100; GET /merchants/{id} 300; /status 20000; POST /interruptions 200; opening-hours 100 |
| 32 | 02-api 2.7 | Uso indevido: 1% em /status e 2% em /merchants e /interruptions; bloqueio em 3 niveis | improper-use | PARCIAL | Limites: <=1% em /events:polling e /merchants/{id}/status; <=2% em /orders/{id}, /confirm, /requestCancellation, /merchants/{id}, /merchants, /interruptions; ack >= 95% (acks/pollings). Janela 24 h. Niveis I, II (15 dias corridos cada) e III (plano em 10 dias). Infracoes: nao enviar ack, consultar pedido inexistente (404), merchant nao autorizado (403), polling < 30 s, credencial na URL. O estudo cita so 2 dos 8 endpoints e omite o polling 1% e o ack 95% |
| 33 | 02-api 2.8 | Polling/ack em /orders:polling (outra pagina) | OA Events; U/order/workflow | CONFIRMADA | OA Events: /events:polling e /events/acknowledgment (nao ha /orders:polling na OA). Seguir a OA |

## Contagem

| Veredito | Qtd |
|---|---|
| CONFIRMADA (inclui 1 com ressalva) | 27 |
| PARCIAL | 5 (#4, #14, #15, #18, #32) |
| REFUTADA | 0 |
| NAO VERIFICAVEL | 1 (#26) |

## Achados relevantes (o autor precisa corrigir)

1. #32 improper-use incompleto: incluir polling <=1%, /orders/{id}, /confirm, /requestCancellation <=2%, ack >= 95%, infracao "polling < 30 s" e janela de 24 h. Muda o desenho do worker (cadencia minima 30 s, nao exceder, e nao consultar pedido inexistente).
2. #18 Beneficio: a doc se contradiz (IFOOD/MERCHANT versus OWN/PARTNER). Hoje o estudo apresenta uma so tabela como regra. Reclassificar como divergencia da doc (secao 2.8 e dicionario 3.2) com prova na loja de teste.
3. #14 Conclusao automatica: acrescentar que, para pedido agendado, os 6 h e 4 h contam a partir do agendamento (e 13 h para mercado e farmacia, fora de escopo).
4. #4 Retencao: dizer "ate 8 h apos a entrega", e que so voltam eventos sem ACK. Acrescentar que evento sem filtro recebe auto-ack.
5. #15 Motivos: nao existe 510; a lista e dinamica (GET /cancellationReasons), nao hardcode 501-512.
6. #19 / N16 (chat com o cliente): a pagina de detalhes do pedido diz "Para contatar o cliente, utilize o chat". O estudo lista "Chat com o cliente: nao encontrado na doc (N16)". Ha mencao na doc; procurar o modulo do chat antes de manter N16 como "nao encontrado".
7. #21: a pagina de homologacao Catalog ainda manda usar PATCH /items/price e /items/status (obsoletos na OA). Acrescentar essa 9a divergencia a secao 2.8.
8. #9 Webhook: acrescentar "sem ack e pode descartar apos varias falhas; usar polling como fallback" (hoje so a retentativa de 15 min).
9. #1: acrescentar a tensao entre fundamentals (agendado conta de preparationStartDateTime) e homologation (8 min independente de orderTiming).

## Afirmacoes sem URL ou com URL errada

- 02-api 2.2 "token de ate 8000 caracteres" (#26): fonte U/authentication/distributed nao traz o numero.
- 02-api 2.4 "Excesso de cancelamento pode fechar a loja temporariamente": nao conferido nesta rodada.
- 02-api 2.8 "G-cat/workflow" e "G-cat/guides/availability" (divergencias de preco/estoque): nao conferidos; a OA confirma o lado correto.
- Siglas G/first-steps/*, G-cat/fundamentals, "Item some do app quando" e `Escopo por modulo`: nao conferidos (fora das ~33 afirmacoes priorizadas).
- Nota de metodo: a pagina U/order/details e U/events/polling-overview tem varios blocos de idioma (en/es/pt) em `allMdx.nodes`; o texto em ingles do polling e a fonte mais completa.
