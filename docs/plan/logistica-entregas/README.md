# Plano: logística de entrega automatizada do EasyStok

Issue: #1597 · Levantamento: 2026-10-11 · Base medida: master `3800108d` · Status: PLANO, aguardando as decisões da seção 9. Nenhuma fatia executada.
Escopo: geolocalização + cadastro do cliente + IA + Lalamove, 99 e Uber + agendamento e janelas.
Relacionados: F16 #1246/#1304 (chaves por loja), F17 #1247 (vira a L2 deste plano), #1205 (motor de integrações), #1322 (TomTom), M8.3 e M8.5 em [09-m8-entregas.md](../erp-casa-da-baba/09-m8-entregas.md).

---

## 1. Veredito

1. **Não falta tecnologia, falta costura.** Geocodificação, rota, janela, vaga, viagem, agente com ferramentas e cofre de credencial já existem. Não existe a porta de logística, o despacho externo, a escolha de provedor, o corte automático da janela nem a medição de custo real.
2. **Economia é número de corridas, não custo de API.** Na capacidade configurada hoje (3 vagas x 2 janelas x 5 dias = ~30 entregas por semana, ~130 por mês, memória de 02/10, NÃO medido hoje) todo o Google Maps cabe nas franquias grátis. O dinheiro está em: uma corrida por janela em vez de uma por pedido, frete cobrado versus custo real, e preço competitivo entre provedores.
3. **Lalamove primeiro, porque é a única com tudo documentado e testável.** Uber Direct é o segundo adapter (contrato existe, preço não). 99 e iFood Envios só depois de resposta comercial. Porta única `ILogisticaGateway` para os quatro.
4. **IA onde há texto livre e exceção, nunca em preço, rota ou ETA.** O LLM extrai endereço, redige aviso e propõe ação com clique humano. Decisão de provedor é regra determinística.
5. **Automação em degraus, com gate.** Começa sugerindo (um clique), só passa a despachar sozinha depois de janelas medidas sem intervenção.

---

## 2. O que já existe e o que falta (medido no repo)

| Peça | Estado | Fonte |
|---|---|---|
| Geocodificação (Google, Nominatim) e rota real (Google Routes) | EXISTE, ports `IGeocodingClient`, `IRotaClient`. TomTom em branch #1322, sem merge | `IGeocodingClient.cs`, `GoogleRotasClient.cs` |
| Endereço e coordenada no pedido | PARCIAL (M8.3): snapshot gravado na criação; falta `PATCH`, revalidação de área e aviso no console | `EnderecoEntregaPedido.cs`, `09-m8-entregas.md` |
| Janela semanal com vaga por data, horizonte 14 dias, sinal de 50% | EXISTE (M8.2) | `JanelaEntrega.cs`, `VagaOcupada.cs` |
| Viagem com paradas, ordem, entregador, "saiu", "entregue", aviso S13 | EXISTE (S44) | `Viagem.cs`, `ViagensUseCases.cs` |
| Rota da viagem | SÓ LINK do Maps; não chama API nem ordena | `RotaMaps.cs` |
| Cofre de credencial por loja (AES-256-GCM) | EXISTE ocioso; F16 #1304 aberta, sem merge | `credencial_integracao` |
| Agente com ferramentas (`listar_janelas`, `criar_pedido`, `consultar_cardapio`) e ação só com clique | EXISTE; ação com clique em branch #1445 | `IAgenteLlmClient`, `IFerramentaAgente` |
| Cadastro do cliente pela conversa (F18) | EXISTE | `CadastrarClienteDaConversaUseCase` |
| `ILogisticaGateway`, corrida externa, seletor de provedor | NÃO EXISTE | #1205 F6, #1247 F17 abertas |
| Coordenada e precisão no endereço do CLIENTE | NÃO EXISTE: `ClienteEndereco` sem lat/lng, fonte nem data | `Cliente.cs:320-343` |
| Localização enviada pelo cliente no WhatsApp | PARSEADA e achatada em texto; não vira coordenada do endereço | `WebhookWhatsAppParser.cs:106,146` |
| Enum de empresa do entregador | Tem Lalamove, 99, iFood; FALTA Uber | `EmpresaEntregador.cs` |
| Corte automático da janela, plano B, timeout de "sem motorista" | NÃO EXISTE | n/a |
| Custo real da corrida x frete cobrado | NÃO EXISTE; M8.5 só mede pontualidade e bairro | `09-m8-entregas.md` |

---

## 3. Provedores: fato confirmado, risco e veredito

Confirmado por mim (leitura direta) em 2026-10-11: PDF do webhook Lalamove (abr/2026, p.7-8), termos Uber Direct BR (06/04/2026), README Corp API v2 da 99, página de preço do Google (atualizada 2026-10-08). O resto vem dos relatórios dos pesquisadores, marcado.

| | Lalamove | Uber Direct | 99 | iFood Envios / Shipping |
|---|---|---|---|---|
| API pública e doc | SIM, v3, sandbox self-service | SIM, doc e termos de API para o Brasil | PARCIAL: Corp API v2 (corrida corporativa com categoria `delivery99` e `delivery-moto99`). Fluxo "99Entrega" dedicado: SÓ indício de terceiros | Shipping: SIM, exige loja iFood. Envios (lançado 16/09/2026): doc NÃO achada |
| Auth | HMAC `hmac KEY:TS:SIG`, `Market: BR` | OAuth2 client_credentials (resumo de busca) | `x-api-key`, exige employeeID e costCenterID | n/a |
| Cotação | `POST /v3/quotations`, 2 a 16 paradas, vale 5 min | `delivery_quotes` (resumo de busca), validade NÃO declarada | `GET /rides/estimate`, devolve FAIXA; exemplo da doc só lista táxi | expira |
| Agendamento | `scheduleAt` UTC até 30 dias | ASAP a partir de 30 min, até 30 dias (resumo de busca) | NÃO achei campo | a partir de 1 h |
| Webhook | 10 eventos, ASSINADO (p.8), 10 tentativas em 24 h e depois DESATIVA a URL | `x-uber-signature` HMAC (resumo de busca) | Basic Auth, SEM HMAC, 10 tentativas em ~2 h | eventos de status |
| Prova de entrega | foto, assinatura, código | assinatura, foto, PIN | não achei | não confirmado |
| Preço | SEM tabela pública. Pré-pago, produção só com saldo (402 sem saldo) | "acordado por escrito". Cancelar R$ 5; espera paga após 10 min | SEM preço público | não divulgado |
| Cobertura BR | 9 cidades: BH, Rio, SP, Porto Alegre, Brasília, Curitiba, Fortaleza, Salvador, Florianópolis | "cidades Uber" (2023, secundário) | 3.300+ municípios (secundário) | não confirmado |
| Limites | Moto 20 kg, 35x40x30 cm. Bolo grande pede carro | Não achei | Moto: R$ 500 por item | n/a |
| Contrato | Conta Business, carteira | 1 ano com renovação automática, MAS qualquer parte sai sem multa com 30 dias de aviso (cl. 8.2). Integrar em 90 dias, conta em 30 | comercial | comercial |
| **Veredito** | **INTEGRAR (L2)** | **CONTATAR e integrar como 2º adapter se o preço fechar (L4)** | **CONTATAR comercial; sem isso, nada** | **CONTATAR** |

Correções a fazer na F17 (#1247) antes de codar:
- "Assinatura do webhook não documentada" está ERRADO. É documentada (validar `apiKey` e `signature`). Fórmula exata: confirmar com payload real do sandbox, porque o timestamp do webhook vem em segundos e o da requisição em ms.
- `POST /v3/orders` NÃO tem idempotência: retry cego duplica corrida e cobrança.
- O `orderId` pode MUDAR: a Lalamove cancela e clona pedido (CANCELED por `LALAMOVE_CUSTOMER_SUPPORT`, depois ORDER_REPLACED com `prevOrderId`). Não tratar como cancelado sem checar a substituição.
- Cancelar só em ASSIGNING_DRIVER ou até 5 min do match; depois, trocar motorista (15 min após o match, antes da coleta, sem custo).
- EXPIRED só 2 h depois de `scheduleAt`: precisamos de timeout próprio muito menor.
- Pesquisadores citaram `DELETE /rides/{id}` na 99; NÃO vi esse endpoint na parte lida do README. Não contar com cancelamento por API da 99 até confirmar.

Descartados: Borzo (saiu do Brasil em 29/09/2025), Mandaê e Flash (parcel/postal), Frenet e Melhor Envio (postal), agregadores de despacho (nenhum com API pública para o Brasil). **A porta `ILogisticaGateway` já é o agregador.** Open Delivery Logistics (spec aberta v1.7.1, OAuth2, `availability`, `delivery`, `cancel`, `deliveryUpdate`, código de confirmação) serve de MOLDE de vocabulário da porta; não construir servidor Open Delivery agora.

---

## 4. Economia

Custo de API (preços Google confirmados na página de 2026-10-08):

| Item | Franquia/mês | Preço excedente | Na nossa escala (~130/mês) |
|---|---|---|---|
| Geocoding | 10.000 | US$ 5 / 1.000 | US$ 0 |
| Compute Routes Essentials | 10.000 | US$ 5 / 1.000 | US$ 0 |
| Compute Route Matrix Essentials | 10.000 elementos | US$ 5 / 1.000 | US$ 0 |
| Pro (waypoint otimizado, trânsito) | 5.000 | US$ 10 / 1.000 | US$ 0 |
| LLM por chamada (texto de endereço) | n/a | frações de centavo | centavos por mês |

Gatilho para trocar de motor de matriz (OSRM local): mais de ~650 entregas por mês (10.000 elementos / 43 janelas = ~14 paradas por janela). Estamos 5x abaixo. **Não adotar OSRM, VROOM, OR-Tools nem Route Optimization agora.**

Alavancas reais, em ordem de retorno esperado:

| # | Alavanca | Como medir | Dependência |
|---|---|---|---|
| 1 | Uma corrida por janela (até 3 paradas) em vez de uma por pedido | preço multiparada da Lalamove (composição NÃO documentada: perguntar ao AM) | L2 |
| 2 | Frete cobrado x custo real, por viagem | relatório novo | L2 + L3 |
| 3 | Próprio x externo pelo custo marginal por parada | configuração da loja | L3 |
| 4 | Dois preços reais por viagem | cotação dos dois provedores na mesma viagem | L4 |
| 5 | Oferecer janela onde o endereço já "carona" em corrida existente | piloto com gate | L5, condicional |

**Hipótese a medir antes de qualquer decisão de preço:** custo de moto de ~8 km. Não existe número oficial (blogs falam em R$ 9,50 até 4 km, provável repasse ao motorista, não confiável). Frete cobrado hoje: R$ 8 até 3 km, 12 até 6 km, 18 até 8 km. Se a corrida custar mais que o frete, cada entrega externa dá prejuízo e só a medição diz quanto.

---

## 5. Arquitetura alvo

```
 Pedido (endereço + coord + destinatário) ──► Janela (data + faixa + vaga)
                         │
                         ▼
              ┌─────────────────────┐   corte da janela (job)
              │  Planejador de      │◄──────────────────────────┐
              │  viagem             │  ≤8 paradas: força bruta  │
              │  (agrupa e ordena)  │  Routes API p/ distância  │
              └─────────┬───────────┘                           │
                        ▼                                       │
              ┌─────────────────────┐                  ┌────────┴────────┐
              │  Política de        │  cotações        │ Relógio de     │
              │  despacho (regra)   │◄────────────┐    │ exceção:       │
              └─────────┬───────────┘             │    │ sem motorista, │
                        │ escolhe                 │    │ cancelada,     │
                        ▼                         │    │ atraso         │
              ┌─────────────────────┐   ┌─────────┴───────┐ └──────────┘
              │  CorridaExterna     │   │ ILogisticaGateway│
              │  (estado, preço,    │──►│ Cotar·Criar·    │
              │  externalId, track) │   │ Consultar·      │
              └─────────┬───────────┘   │ Cancelar·       │
                        ▲               │ ValidarWebhook  │
   webhook assinado ────┘               └──┬────┬────┬────┘
   (ack 200, dedupe, REconsulta)           │    │    │
                                       Lalamove Uber 99/iFood
                                       (L2)    (L4) (se comercial)
                                       + Próprio/Manual (fluxo de hoje)
```

Decisões de desenho:

| # | Decisão | Por quê |
|---|---|---|
| A1 | `CorridaExterna` é entidade NOVA ligada à `Viagem` (1 viagem, N tentativas), não colunas na `Viagem` | Re-despacho e troca de provedor preservam histórico; `Viagem` segue a verdade operacional das paradas |
| A2 | `ILogisticaGateway` com `Capacidades` (agenda, POD, veículos, teto de valor por item) e `Cotacao{min,max,quoteId?,expiraEm?,etaColeta?}` | 99 devolve faixa, Open Delivery não tem validade, iFood expira: o modelo não pode supor a cotação da Lalamove |
| A3 | Webhook NUNCA é fonte da verdade: ack 200 primeiro, validar assinatura, dedupe por `eventId`, ordenar por `updatedAt`, **reconsultar** o recurso e só então mudar estado | Evento chega fora de ordem e pode ser forjado |
| A4 | Antes do `POST` de corrida, gravar a tentativa (`Criando`) com o ID interno em `metadata`. Em timeout, NÃO repetir; reconciliar por webhook `ORDER_CREATED` ou consulta | Sem idempotência na Lalamove, retry cego paga duas corridas |
| A5 | Heartbeat do webhook: polling de contingência por corrida ativa + alerta quando a URL for desativada | Lalamove desativa a URL em silêncio após 10 falhas |
| A6 | Política de despacho determinística (preço, prazo, veículo exigido, teto de valor do item, saúde do provedor, custo marginal do próprio) | Auditável, testável, explicável ao dono; LLM só redige a explicação |
| A7 | Credencial por loja pelo `credencial_integracao` (F16) | Já decidido; D2 do #1205 |
| A8 | Coordenada do endereço do cliente guarda `fonte`, `precisão` e `geocodificadoEm`; fonte Google expira em 30 dias | Termos do Google limitam cache de lat/lng a 30 dias (cláusula de 2023, vigente NÃO CONFIRMADA). Pin do cliente e CNEFE não têm essa limitação |
| A9 | Vigia de fonte única: ports de geocode e rota ficam como estão; TomTom (#1322) entra por configuração, sem adapter novo aqui | Evita duplicar WIP de outra sessão |

Dados novos (todos aditivos, migration tier ALTO):
- `corrida_externa`, `evento_corrida` (dedupe e payload bruto).
- `cliente_endereco`: `Lat`, `Lng`, `PrecisaoGeo`, `FonteGeo`, `GeocodificadoEm`.
- Configuração da loja: custo marginal do entregador próprio, antecedência do corte, teto de preço aceito, nível de autonomia.
- `EmpresaEntregador.Uber`.
- Pedido: CONFIRMAR se nome e telefone do pedido são do destinatário (presente de confeitaria é caso comum e a Lalamove exige nome e telefone do destinatário em E.164).

---

## 6. Fluxo do dia (janela 9h às 12h, valores iniciais a calibrar)

| Quando | O que acontece | Quem |
|---|---|---|
| D-1 18h | Congela a lista da janela; prévia de agrupamento e custo INDICATIVO | sistema |
| D 07h30 (corte, ~90 min antes) | Planeja viagem, cota todos os provedores aptos, escolhe, gera `scheduleAt` do momento de coleta. L3-nível 1: mostra a proposta e espera UM clique. Nível 2: despacha sozinho dentro do teto | sistema + atendente |
| Criação | Cotação vale 5 min: cotar e criar na mesma sequência. Entrega imediata (coleta em menos de 30 min) não usa `scheduleAt` | sistema |
| T-30 min sem motorista | Gorjeta (priority fee) em degrau; recota o outro provedor | sistema |
| T-10 min sem motorista | Plano B: outro provedor ou entregador próprio; alerta no console e lembrete da dona | sistema |
| Coleta (`PICKED_UP`) | Pedidos vão a "saiu para entrega" e o cliente recebe o aviso S13 COM o link de rastreio do provedor | sistema |
| Entrega (`COMPLETED`) | Pedidos entregues; custo real gravado; pontualidade (M8.5) calculada | sistema |

Mapeamento de estado Lalamove (corrige a tabela da F17): `ASSIGNING_DRIVER` chamado aberto; `ON_GOING` motorista a caminho da cozinha; `PICKED_UP` saiu; `COMPLETED` entregue; `REJECTED` e `EXPIRED` sem entregador, aciona plano B; `CANCELED` só vale depois de checar `ORDER_REPLACED`.

---

## 7. IA: onde entra e onde não entra

| Entra (texto livre ou exceção) | Mecanismo | Não entra (determinístico) |
|---|---|---|
| Extrair campos de endereço da mensagem | já existe na S14; validado pelo geocoder, nunca produz coordenada | Distância, frete, preço, ETA |
| Pedir PIN quando o geocode for fraco | pergunta guiada (condomínio, sem número, ponto de referência). Mensagem interativa `location_request_message` da Meta: NÃO CONFIRMADO, validar na doc antes | Escolha de provedor |
| Ponto de referência e complemento | extrai para o campo de observação do entregador; humano ou PIN confirma | Ordem das paradas |
| Aviso de exceção ao cliente (atraso, troca de motorista) | rascunho do agente, envio só com clique da atendente (padrão da #1445) | Cobrança e estorno |
| Endereço suspeito antes de despachar | regra primeiro (precisão ruim, mais de 15 km da loja, cidade diferente); LLM só explica | Janela e vaga |
| Ferramentas novas do agente | `validar_endereco` (existe), `status_da_entrega` (novo, lê `CorridaExterna`), `sugerir_janela` (existe como `listar_janelas`) | |

---

## 8. Fases com gate (fatias no estilo do repo)

Nomeei `L0` a `L5` para não colidir com M8.x e F17. Cada uma: PR própria, rollback por revert.

| Fatia | Entrega | Tier | Gate de aceite | Depende de |
|---|---|---|---|---|
| **L0** Decisões e contatos | Seção 9 e 10 respondidas; conta Lalamove Business, chave sandbox, perguntas ao comercial | n/a | decisões gravadas | Felipe |
| **L1** Fundação | `ILogisticaGateway`, `CorridaExterna`, `evento_corrida`, adapter Próprio/Manual (preserva o fluxo atual), provedor fake para teste, ingresso de webhook genérico, vigia de heartbeat | ALTO (migration) | viagem própria funciona igual a hoje; fake percorre todos os estados; webhook com corpo falso não muda nada sem reconsulta | F16 #1304, M8.3 fechada |
| **L1b** Geodado do cliente | Colunas de coordenada no endereço, PIN do WhatsApp vira coordenada, expiração de 30 dias para fonte Google | ALTO (migration) | cliente recorrente não regeocodifica; PIN vence o geocoder | pode rodar junto da L1 |
| **L2** Lalamove | Adapter v3 (cotar, criar, consultar, cancelar, webhook assinado), `THERMAL_BAG_1`, veículo por classe do pedido, sombra de 30 dias (cota sem criar) | ALTO | sandbox: 2 pedidos, 3 paradas, entregue; teste da assinatura contra payload real; cancelar fora da janela mostra motivo em português; 402 vira alerta | L1, conta e saldo |
| **L3** Corte automático | Job de corte, planejador (força bruta até 8 paradas), política de despacho, timeouts, plano B, custo x frete no relatório | ALTO | nível 1 (um clique). Passa ao nível 2 após 10 janelas reais sem correção manual do despacho | L2 |
| **L4** Segundo provedor | Adapter Uber Direct (ou 99 se o comercial entregar API) | ALTO | HIPÓTESE: dois preços reais reduzem custo mediano em 10% ou cortam pela metade o "sem motorista". TETO: 30 dias de medição. ABANDONO: abaixo disso, mantém um provedor | L2 + preço fechado |
| **L5** Otimização condicional | OSRM municipal, OR-Tools, oferta de janela por cluster, ETA por EWMA | MÉDIO | SÓ abrir se medir mais de 8 paradas por janela OU mais de 650 entregas/mês OU frete subsidiado acima de um teto definido | dados da L3 |

Paralelo, sem dependência: M8.4 (tela do entregador próprio por link) segue seu plano; M8.5 ganha a coluna de custo real quando L3 existir.

---

## 9. Decisões do Felipe (múltipla escolha)

**D-L1 · Quem absorve a diferença entre o custo real da corrida e o frete cobrado?**
- A) (Recomendado) Medir 30 dias em sombra e decidir com número; frete atual continua.
- B) Repassar a cotação real ao cliente nas entregas externas, com piso e teto.
- C) Manter a faixa R$ 8/12/18 e absorver sempre.

**D-L2 · Entregador próprio ou externo como padrão?**
- A) (Recomendado) Próprio quando houver disponível e a viagem couber; externo como excedente e plano B.
- B) Externo sempre, para simplificar a operação.
- C) O mais barato por viagem, comparando custo marginal do próprio com a cotação.

**D-L3 · Autonomia do despacho**
- A) (Recomendado) Nível 1: sistema propõe, atendente confirma com um clique; sobe para o 2 depois do gate da L3.
- B) Nível 2 desde o início, dentro de um teto de preço.
- C) Só manual, com cotação exibida.

**D-L4 · Segundo provedor**
- A) (Recomendado) Contatar Uber e 99 agora, integrar a que trouxer preço e API; decidir na L4.
- B) Só Lalamove; reabrir se o preço ou a taxa de "sem motorista" ficar ruim.
- C) Uber Direct desde já, em paralelo à Lalamove.

**D-L5 · Cidade e cobertura.** Lalamove cobre 9 cidades no BR. Confirmar que a Casa da Baba está em uma delas (a nota do ADR-0011 cita SP).

---

## 10. Perguntas comerciais (só o Felipe consegue)

| Para | Pergunta exata |
|---|---|
| Lalamove (partner.support@lalamove.com, sales.br@lalamove.com) | Tabela e taxa mínima de moto, 8 km, na minha cidade. Como `base`, `surcharge` e `vat` somam em multiparada. Aceita pós-pago ou faturamento? Recarga mínima e Pix na carteira? CNPJ obrigatório e prazo de aprovação? Antecedência mínima e quando o pedido agendado entra em busca. Taxa e regra de cancelamento vigentes na API. Como forçar estados e disparar webhook no sandbox. Máximo real de paradas. IPs de origem do webhook |
| Uber Direct | Tarifa de moto até 8 km na minha cidade, taxa de agendamento, validade da cotação, faturamento sem cartão, CNPJ obrigatório |
| 99 (gerente 99 Empresas ou api-suporte@99app.com) | Existe API de 99Entrega moto distinta da Corp v2, com cotação, criação, cancelamento e webhook assinado? Sandbox cria entrega? Agenda hora futura? Preço fechado ou faixa? Limite acima de R$ 500 por item para doces? |
| iFood Envios | API pública e doc? Exige loja iFood? Tarifa, antecedência de agendamento, cidades, bag térmica |

---

## 11. Riscos

| Risco | Mitigação |
|---|---|
| Corrida custa mais que o frete e ninguém vê | Sombra de 30 dias antes de despachar; relatório custo x frete na L3 |
| Retry duplica corrida e cobrança | A4: tentativa gravada antes do `POST`, sem retry cego |
| Webhook morre em silêncio | A5: polling de contingência e alerta |
| `orderId` muda e a entrega parece cancelada | Guardar ID como string, seguir `prevOrderId` |
| Bolo não cabe na moto (20 kg, 35 a 40 cm) | Classe de volume do pedido define `serviceType`; `THERMAL_BAG_1` |
| Cache de coordenada do Google acima de 30 dias | A8: fonte e data na coordenada; PIN e CNEFE primeiro |
| CEP único de cidade pequena e condomínio sem número | Rebaixar precisão, pedir PIN; nunca despachar com precisão "cidade" |
| LGPD: nome, telefone e endereço vão ao provedor; `shareLink` é público | Enviar só o necessário; base legal de execução do contrato; não repetir o link em canal aberto |
| Uber: 90 dias para integrar depois de aceitar | Só aceitar os termos quando houver sprint para a L4 |
| Conflito com WIP de outras sessões (#1304, #1247, #1322, #1445) | L1 só começa depois do merge da F16; F17 vira a L2 deste plano |

---

## 12. Não confirmado (não decidir em cima disto)

- Preço de corrida de moto em R$ de qualquer provedor.
- Composição do preço multiparada e máximo de paradas da Lalamove no BR.
- Fórmula exata da assinatura do webhook Lalamove (existe e é documentada; testar com payload real).
- Uber: endpoints, validade da cotação e cobertura de cidades (fonte: resumos de busca; termos BR lidos por mim).
- 99: cancelamento por API, agendamento, sandbox que cria corrida, preço, API de 99Entrega distinta da Corp.
- iFood Envios: existência de doc pública.
- Mensagem interativa de pedir localização no WhatsApp.
- Licença do geocodebr/CNEFE para uso comercial; cláusula vigente de cache do Google.
- Capacidade configurada em produção hoje (vem da memória de 02/10).

---

## Fontes principais

- Lalamove: https://developers.lalamove.com/ · webhook v1.5 (abr/2026) https://developers.lalamove.com/files/v3_Webhook_v1.5.pdf · veículos BR https://developers.lalamove.com/files/specialRequests_Key_Update_2023_BR.pdf
- Uber Direct termos BR: https://www.uber.com/legal/en/document/?country=brazil&lang=pt-br&name=uber-direct-api-terms-and-conditions · docs https://developer.uber.com/docs/deliveries/guides/authentication
- 99 Corp API v2: https://github.com/99Taxis/corp-api-v2-documentation · contato https://99app.com/ajuda/empresa/como-solicitar/
- Open Delivery: https://github.com/Abrasel-Nacional/opendelivery
- iFood Shipping: https://developer.ifood.com.br/en-US/docs/food/guides/modules/shipping/inside
- Google Maps preço: https://developers.google.com/maps/billing-and-pricing/pricing
- CNEFE/IBGE e geocodebr: https://github.com/ipeaGIT/geocodebr
- Planos do repo: `docs/plan/erp-casa-da-baba/09-m8-entregas.md`, `docs/plan/atendimento-whatsapp/11-console-fechamento.md` (F16, F17), `docs/plan/integracoes/README.md`
