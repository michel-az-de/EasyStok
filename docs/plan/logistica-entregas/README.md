# Plano: logística de entrega automatizada do EasyStok

Issue: #1597 · Levantamento: 2026-10-11 · Base medida: master `0717abfc` · Versão 2 (ordem invertida após revisão adversarial).
Status: PLANO, aguardando as decisões da seção 8. Nenhuma fatia executada.
Escopo: geolocalização + cadastro do cliente + IA + Lalamove, 99 e Uber + agendamento e janelas.
Relacionados: F16 #1246/#1304 (chaves por loja), F17 #1247 (vira a L3 deste plano), #1205 (motor de integrações), #1322 (TomTom), [09-m8-entregas.md](../erp-casa-da-baba/09-m8-entregas.md).

---

## 1. Veredito

1. **PREÇO ANTES DE CÓDIGO.** Não existe um único preço de corrida em fonte oficial. As três primeiras fatias (L0 a L2) medem volume, custo e operação sem migration e sem contrato. Só depois entra código.
2. **A PRIMEIRA ENTREGA DE CÓDIGO É PEQUENA:** um adapter Lalamove com botão "Chamar corrida" na viagem e o custo gravado. Porta genérica, segundo provedor e automação só abrem com número que os justifique.
3. **O DOMÍNIO ATUAL NÃO COMPORTA CORRIDA EXTERNA.** A viagem não sai sem entregador cadastrado e não tem estado de "entrega não realizada". Isso se resolve na L3, com ADR, antes de qualquer chamada ao provedor.
4. **ECONOMIA É FRETE CONTRA CUSTO REAL**, não custo de API: o Google Maps cabe nas franquias grátis nesta escala.
5. **IA SÓ EM TEXTO LIVRE.** Preço, rota, provedor e despacho são regra determinística, e despacho pago exige humano.

O que a versão 1 errou (registrado para não repetir): fundação genérica antes de preço; planejador de rota próprio que o provedor já faz; corte por relógio; "ack 200 primeiro" no webhook; "nunca repetir o POST" com retry já ligado; rollback "por revert" onde revert não desfaz.

---

## 2. O que existe e o que trava (conferido no código)

| Peça | Estado | Fonte |
|---|---|---|
| Geocodificação e rota | EXISTE. `IRotaClient` mede UM par origem e destino; `GeocodeResultado` só tem `Confiavel` | `IGeocodingClient.cs:20-23`, `IRotaClient.cs` |
| Endereço e coordenada no pedido | PARCIAL (M8.3): snapshot na criação; falta `PATCH` e revalidação | `EnderecoEntregaPedido.cs` |
| Janela semanal, vaga por data, 14 dias, sinal de 50% | EXISTE (M8.2). Janela é `TimeOnly` sem fuso | `JanelaEntrega.cs:27-28` |
| Viagem, paradas, "saiu", "entregue", aviso S13 | EXISTE (S44) | `Viagem.cs`, `ViagensUseCases.cs` |
| **Viagem só sai com `Entregador` cadastrado e ativo (RN-32)** e grava o retrato dele em cada parada | TRAVA corrida externa: o motorista do provedor só existe depois do match | `Viagem.cs:82-93`, `ParadaViagem.cs:36-42` |
| **Não há saída de `EmRota` que não seja "tudo entregue"**; `SaiuParaEntrega` só vai a `Entregue` ou `Cancelado` | TRAVA cliente ausente, devolução e corrida cancelada após a coleta | `Viagem.cs:95-112`, `PedidoStateMachine.cs:47` |
| Webhook genérico com dedupe | JÁ EXISTE: `api/webhooks/{provedor}`, `WebhookRecebido`. Valida antes de responder; 200 sem processar faz o provedor parar de reenviar (#787) | `WebhookGatewayController.cs:33-45,86-89` |
| Validador de assinatura | Síncrono e SEM tenant; o do Mercado Pago lê segredo global | `IGatewayWebhookProcessor.cs:44-54` |
| Resiliência HTTP | Categoria `logistics` JÁ recebe retry de 3 tentativas. `whatsapp-envio` foi tirado disso porque POST não é idempotente (#1292) | `IntegrationCategories.cs:19-31` |
| Jobs | API e Worker registram o mesmo serviço; quem não pode duplicar usa advisory lock (#1575) | `LockKeys.cs:66-82` |
| Cofre de credencial por loja | EXISTE e é usado pelo WhatsApp; chaves de logística dependem da F16 #1304 | `IIntegrationCredentialResolver` |
| Destinatário no pedido | NÃO EXISTE: só snapshot do comprador (`ClienteNome`, `ClienteApt`, `ClienteTelefone`) | `Pedido.cs:41-43` |
| Coordenada no endereço do cliente | NÃO EXISTE | `Cliente.cs:320-343` |
| Localização do WhatsApp | Parseada e achatada em texto. ADR-0017 proíbe aceitar lat/lng do cliente | `WebhookWhatsAppParser.cs:106,146`, `IGeocodingClient.cs:4-5` |
| `ILogisticaGateway`, corrida externa, custo real da corrida | NÃO EXISTE | #1205 F6, #1247 |
| Enum de empresa do entregador | Tem Lalamove, 99, iFood; falta Uber | `EmpresaEntregador.cs:7-14` |

---

## 3. Provedores

Confirmado por leitura direta em 2026-10-11: PDF do webhook Lalamove (abr/2026, p.7-8), termos Uber Direct BR (06/04/2026), README da Corp API v2 da 99, página de preço do Google (2026-10-08). O resto é relato de pesquisa, marcado.

| | Lalamove | Uber Direct | 99 |
|---|---|---|---|
| API e doc | v3, sandbox self-service | Doc e termos de API para o Brasil | Só Corp API v2 (corrida corporativa com `delivery99` e `delivery-moto99`). API de 99Entrega dedicada: só indício de terceiros |
| Cotação | `POST /v3/quotations`, vale 5 min, `isRouteOptimized` ordena as paradas; 100/min em produção | `delivery_quotes` (relato) | `GET /rides/estimate`, devolve faixa |
| Agendamento | `scheduleAt` UTC, até 30 dias | de 30 min a 30 dias (relato) | NÃO tem |
| Cancelar | Só em `ASSIGNING_DRIVER` ou até 5 min do match. Depois, trocar motorista (15 min após o match, antes da coleta) | R$ 5 após o entregador chegar | `DELETE /rides/{id}` existe |
| Webhook | Assinado (`apiKey` + `signature`), 10 tentativas em 24 h e depois DESATIVA a URL, pode chegar fora de ordem | `x-uber-signature` HMAC (relato) | Basic Auth, sem HMAC, 10 tentativas em ~2 h |
| Preço | SEM tabela pública. Pré-pago: chave de produção só após recarga; 402 sem saldo | "Acordado por escrito". Espera paga a partir do 10º minuto. **A empresa paga o retorno** do item não entregue (cl. 4.1) e a Uber **se exime de dano** (cl. 2.8, 5.3) | Sem preço público |
| Cobertura BR | 9 cidades: BH, Rio, SP, Porto Alegre, Brasília, Curitiba, Fortaleza, Salvador, Florianópolis | "cidades Uber" (2023, secundário) | 3.300+ municípios (secundário) |
| Sandbox | Simulação de motorista NÃO documentada | n/a | NÃO cria corrida: teste é real e pago |
| Contrato | Conta Business e carteira | 1 ano com renovação; sai sem multa com 30 dias (cl. 8.2); 90 dias para integrar depois de aceitar | Comercial |
| Veredito | **Primeiro e único até a L5** | Contatar; adapter só na L5 | Contatar; sem resposta, nada |

Fatos da Lalamove que mudam a F17 (#1247):
- A assinatura do webhook É documentada. A fórmula exata se confirma com payload real: o timestamp do webhook vem em segundos, o da requisição em ms.
- `POST /v3/orders` não tem idempotência (`Request-ID` é só nonce de suporte).
- O `orderId` pode mudar: `CANCELED` por `LALAMOVE_CUSTOMER_SUPPORT`, depois `ORDER_REPLACED` com `prevOrderId`.
- `EXPIRED` só 2 h depois do `scheduleAt`.
- Moto: 20 kg, 35x40x30 cm. Existe `THERMAL_BAG_1`. Perecível é risco do remetente.
- Máximo de paradas: a própria doc se contradiz (15, 16 ou 17). Tratar como NÃO CONFIRMADO.

Descartados: Borzo (saiu do Brasil em 29/09/2025), Mandaê, Flash, Frenet e Melhor Envio (postal), agregadores (nenhum com API pública no Brasil). iFood Envios (16/09/2026) sem doc pública.

---

## 4. Economia

| Item Google | Franquia/mês | Excedente | Nesta escala |
|---|---|---|---|
| Geocoding, Compute Routes e Route Matrix Essentials | 10.000 cada | US$ 5 / 1.000 | US$ 0 |
| Pro (waypoint otimizado, trânsito) | 5.000 | US$ 10 / 1.000 | US$ 0 |

Volume: a capacidade CONFIGURADA é ~130 entregas/mês (3 vagas x 2 janelas x 5 dias, memória de 02/10). Entregas REALIZADAS e quem as fez: NÃO MEDIDO, é a L0.

HIPÓTESES, nenhuma é fato até a L1 e a L2 medirem:

| # | Hipótese | Como a L1/L2 mede |
|---|---|---|
| H1 | A corrida de moto custa mais que o frete cobrado (R$ 8 / 12 / 18) | Cotação dos endereços já entregues contra o frete de cada um |
| H2 | Uma corrida de 3 paradas custa menos que 3 corridas | Mesmos endereços cotados 1x3 e 3x1 |
| H3 | Agrupar não derruba a pontualidade nem estraga produto | Semana manual: ausente na parada 1 atrasa as outras? dano? |
| H4 | O entregador próprio custa menos por parada que o externo | Custo real do próprio informado pelo Felipe contra H1 |

Não adotar OSRM, VROOM, OR-Tools nem Route Optimization: sem volume que justifique.

---

## 5. Fases (ordem invertida)

| Fatia | Entrega | Código | Gate para abrir a próxima |
|---|---|---|---|
| **L0** Medir a casa | Cidade; entregas realizadas em 60 dias por janela e por quem entregou; fração paga antes da entrega | 1 consulta de leitura | Cidade coberta pela Lalamove. Se não, o plano troca de provedor antes de seguir |
| **L1** Cotação retroativa | Script DESCARTÁVEL, fora da solução, que cota na Lalamove os endereços já entregues: 1 parada e agrupado por janela. Saída: custo x frete por faixa | Script, sem merge de código de produto | H1 e H2 respondidas com número. Exige conta, chave de produção e recarga mínima (ou confirmar que o sandbox devolve preço real) |
| **L2** Semana manual | 1 semana de corridas pedidas À MÃO no app da Lalamove, anotando custo, tempo até o match, espera, dano, ausência | Nenhum | H3 respondida. Decisões D1 a D4 tomadas com esses números |
| **L3** Adapter mínimo | Botão "Chamar corrida" na viagem: cota, mostra o preço, atendente confirma, cria, acompanha, grava o custo. Um provedor, sem porta genérica | ALTO (migration, ADR) | 20 corridas reais sem duplicidade, sem pedido preso, custo gravado em 100% |
| **L4** Automação por prontidão | Proposta de despacho quando o ÚLTIMO pedido da viagem fica "Pronto"; timeout de "sem motorista" com alerta | MÉDIO | Só abre se a L3 mostrar que o clique manual é o gargalo |
| **L5** Segundo provedor | Extrai `ILogisticaGateway` do adapter existente; adapter Uber ou 99 | ALTO | Só abre com preço fechado do segundo provedor E taxa de "sem motorista" ou custo da L3 que o justifique |

Fora de qualquer fatia até haver número: planejador de rota próprio, CNEFE local, oferta de janela por cluster, ETA por histórico, Open Delivery.

### L3 em detalhe (a única com desenho, porque é a primeira com código)

Pré-requisitos: F16 #1304 mergeada (chave por loja); L2 concluída; decisões D1 a D4.

| # | Problema no código | Resolução na L3 |
|---|---|---|
| R1 | RN-32: viagem não sai sem `Entregador` | ADR novo. Recomendado: `Viagem.SairComCorrida(retrato externo)` com o motorista vindo do provedor; alternativa: entregador sintético por provedor |
| R2 | Sem estado de entrega não realizada | Parada ganha desfecho "não entregue" com motivo; pedido volta a ficar despachável. Mexe em `PedidoStateMachine`: tier ALTO |
| R3 | Retry de 3 tentativas na categoria `logistics` | Categoria nova sem retry para o POST de criação, como `whatsapp-envio`, com teste |
| R4 | Dois cliques ou dois hosts criam duas corridas | Tentativa gravada ANTES do POST, com o ID interno em `metadata`; índice único parcial "uma corrida ativa por viagem"; em timeout não repete, reconcilia por consulta |
| R5 | Webhook: tenant e contrato | Reusar `WebhookGatewayController` e `WebhookRecebido`. Tenant por token secreto da loja na URL (como a F17 previa), o que exige estender o validador para receber o segredo da loja. Validar e persistir ANTES de responder; reconsultar `GET /v3/orders/{id}` antes de mudar estado |
| R6 | Webhook desativado em silêncio | Consulta de contingência para corrida ativa, com advisory lock (padrão #1575), e alerta |
| R7 | `COMPLETED` é um só para N paradas | L3 começa com UMA parada por corrida. Multiparada só entra se H2 e H3 passarem, lendo o desfecho por parada na reconsulta |
| R8 | `PICKED_UP` com pedido ainda em `Preparando` derruba a transação inteira | Corrida só pode ser chamada com todos os pedidos da viagem em `Pronto`; a saída por webhook trata cada pedido separado e alerta o que ficou para trás |
| R9 | Destinatário | Colunas de nome e telefone do destinatário no pedido (presente). Sem elas o provedor liga para o comprador |
| R10 | Fuso | `scheduleAt` em UTC a partir da janela em America/Sao_Paulo, pelo conversor de fuso já usado no projeto (nomear no ADR) |
| R11 | Volume do produto | Atendente escolhe moto ou carro ao chamar; sem campo novo no produto nesta fatia |
| R12 | Dados pessoais | Payload do webhook guardado sem nome e telefone, ou com retenção definida; enviar ao provedor só nome, telefone e endereço da entrega |

Rollback da L3, por camada (revert sozinho NÃO basta):
1. Desligar pela flag da loja: o botão some e o fluxo manual de hoje continua.
2. Cancelar ou concluir à mão as corridas ativas no painel do provedor.
3. Remover a URL de webhook no Partner Portal.
4. Só então revert do código; migration aditiva fica (colunas nulas).

---

## 6. Exceções da entrega externa (a decidir na seção 8, com os números da L2)

| Exceção | O que a L3 faz | Quem paga: decisão |
|---|---|---|
| Sem motorista | Alerta após o tempo definido na L2; atendente escolhe esperar, gorjeta ou outro meio. Nada automático que gaste | n/a |
| Cozinha atrasou | Corrida só é chamada com tudo `Pronto` (R8) | Espera paga, se houver: D3 |
| Cliente ausente | Parada "não entregue" (R2); produto volta | Retorno e reentrega: D3 |
| Produto danificado | Registro na parada, com foto se o provedor der | Refação: D2 |
| Corrida cancelada pelo provedor | Checar `ORDER_REPLACED` antes de tratar como cancelada | n/a |
| Saldo de 50% a receber na entrega | Entrega externa NÃO recebe dinheiro | D1 |

---

## 7. IA: onde entra e onde não entra

| Entra | Não entra |
|---|---|
| Extrair campos de endereço da mensagem (já existe, S14) | Preço, frete, rota, ETA, escolha de provedor |
| Devolver o endereço entendido ao cliente e pedir confirmação antes do pedido: geocoder confiante não prova que é o endereço certo | Chamar corrida: sempre clique humano na L3 |
| Rascunho de aviso de exceção, enviado só com clique (padrão da #1445) | Qualquer gasto |
| `status_da_entrega` como ferramenta de leitura | Explicar suspeita ou escolha: texto fixo resolve |

Riscos tratados: texto do cliente que vira observação enviada ao provedor é limitado em tamanho e saneado; localização enviada pelo cliente NÃO substitui o geocode (ADR-0017), no máximo vira alerta quando diverge dele.

---

## 8. Decisões do Felipe

Pré-condição (não é escolha, é fato a informar): **em que cidade a Casa da Baba entrega.**

**D1 · Saldo de 50% quando a entrega é por terceiro**
- A) (Recomendado) Entrega externa só com pedido 100% pago antes de chamar a corrida.
- B) Saldo por link ou Pix enviado ao cliente na saída, entrega segue mesmo sem pagar.
- C) Entrega externa só para pedido do dia já pago; encomenda com sinal fica com entregador próprio.

**D2 · Produto danificado na entrega externa**
- A) (Recomendado) A casa refaz e absorve; registra por provedor para decidir se continua com ele.
- B) Produto frágil ou grande só vai de carro ou com entregador próprio.
- C) Os dois.

**D3 · Cliente ausente**
- A) (Recomendado) Motorista espera o tempo gratuito, produto volta, reentrega cobrada do cliente.
- B) Produto volta e a casa absorve a reentrega.
- C) Sem reentrega: cliente retira na loja.

**D4 · Frete contra custo real** (decidir DEPOIS da L1)
- A) Manter R$ 8 / 12 / 18 e absorver a diferença.
- B) Reajustar a tabela pelo custo medido.
- C) Repassar a cotação real na entrega externa, com piso e teto.
- D) Pedido mínimo para entrega e incentivo à retirada.

**D5 · Recarga inicial da carteira Lalamove** (valor em reais; exigida para a chave de produção).

**D6 · Presente:** o destinatário pode receber ligação ou mensagem do motorista, ou a observação pede para não avisar?

**D7 · Entregador próprio ou externo como padrão** (decidir DEPOIS da L0 e L1, com H4): hoje existe entregador próprio disponível? quanto custa por saída?

---

## 9. Perguntas comerciais (só o Felipe consegue)

| Para | Pergunta |
|---|---|
| Lalamove (partner.support@lalamove.com, sales.br@lalamove.com) | Tabela e taxa mínima de moto na minha cidade. Como o preço soma em multiparada. O sandbox devolve preço real? Recarga mínima e Pix. CNPJ e prazo de aprovação. Política de cliente ausente, espera e retorno. Antecedência mínima do agendado. Como forçar estados e webhook no sandbox. Máximo de paradas |
| Uber Direct | Tarifa de moto até 8 km, validade da cotação, faturamento sem cartão. Só aceitar os termos quando a L5 abrir (90 dias para integrar) |
| 99 (api-suporte@99app.com) | Existe API de 99Entrega distinta da Corp v2, com cotação fechada, webhook assinado e agendamento? Limite de valor por item na moto |

---

## 10. Riscos

| Risco | Mitigação |
|---|---|
| Corrida custa mais que o frete | L1 mede antes de qualquer código |
| Corrida duplicada e cobrada | R3 e R4 |
| Pedido preso em "saiu para entrega" | R2 |
| Webhook forjado ou de outra loja | R5 |
| Lalamove não cobre a cidade | Pré-condição da L0 |
| Dano em produto perecível | L2 mede; D2 decide |
| Conflito com WIP (#1304, #1247, #1322, #1445) | L3 só depois do merge da F16; F17 passa a ser a L3 |
| Dados pessoais no provedor e no log | R12; link de rastreio só ao próprio cliente |

---

## 11. Não confirmado

- Preço em reais de qualquer provedor; se o sandbox da Lalamove devolve preço real.
- Composição do preço multiparada e máximo de paradas da Lalamove.
- Política da Lalamove para ausente, espera e retorno.
- Fórmula exata da assinatura do webhook Lalamove.
- Uber: endpoints, validade da cotação, cidades (termos BR foram lidos).
- 99: API de 99Entrega dedicada, preço, limite de R$ 500 por item (não está no README).
- Entregas realizadas por mês e quem as faz hoje.
- Cláusula vigente do Google sobre cache de coordenada (lida a de 2023: 30 dias).

---

## Fontes

- Lalamove: https://developers.lalamove.com/ · https://developers.lalamove.com/files/v3_Webhook_v1.5.pdf · https://developers.lalamove.com/files/specialRequests_Key_Update_2023_BR.pdf
- Uber Direct termos BR: https://www.uber.com/legal/en/document/?country=brazil&lang=pt-br&name=uber-direct-api-terms-and-conditions
- 99 Corp API v2: https://github.com/99Taxis/corp-api-v2-documentation
- Google Maps preço: https://developers.google.com/maps/billing-and-pricing/pricing
- Repo: `docs/plan/erp-casa-da-baba/09-m8-entregas.md`, `docs/plan/atendimento-whatsapp/11-console-fechamento.md` (F16, F17), `docs/plan/integracoes/README.md`
