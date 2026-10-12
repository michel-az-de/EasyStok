# M6 Campanhas: promoções, cupons, CRM, mensagens e resultados

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · Data: 2026-10-01
Base medida: master `b713263a` (worktree `erp-casa-da-baba-1316`). Índice: [README](README.md).
Plano irmão dono de S28-S31, S38, S39, S42 e F15: [06-campanhas](../atendimento-whatsapp/06-campanhas.md),
[09-sistema-completo](../atendimento-whatsapp/09-sistema-completo.md),
[11-console-fechamento](../atendimento-whatsapp/11-console-fechamento.md). Este doc **não reescreve** essas
fatias: mapeia o que elas cobrem e especifica só o que falta (M6.1 a M6.7).

Legenda: ✅ vivo · 🟡 parcial ou em PR · ⬜ ausente. **Fato** cita `path:linha` medido nesta data;
**(inferência)** marca o que não foi reproduzido.

## 1. Veredito

**O MOTOR DE CAMPANHA ESTÁ PRONTO E NINGUÉM CONSEGUE USAR: ZERO TELAS.** S28, S29 e S30 estão mergeadas
(#1168, #1198, #1206, mais #1249), mas nenhum front chama `api/campanhas` (`git grep "api/campanhas"` em
`EasyStock.Console/src`, `EasyStock.Web` e `Api/wwwroot`: 0 ocorrências). Promoção de preço não existe em
camada nenhuma, cupom e fidelidade só existem no front (F15 sem PR), e o resultado da campanha é um status
por destinatário, sem painel.

| Sub-item do rascunho | Backend | Console | Gap principal |
|---|---|---|---|
| Promoções | ⬜ | ⬜ | não há preço com período; `CardapioItem` só tem `PrecoStorefront` (`CardapioItem.cs:101`) |
| Cupons | ⬜ (o `Cupom` é do SaaS) | 🟡 só front | F15 (#1245) sem PR |
| CRM | ✅ tags, interesse, consentimento | 🟡 só na ficha | sem lista de clientes filtrável |
| Mensagens | ✅ ondas, limite 7 dias, outbox | ⬜ | sem tela; template Meta em texto livre |
| Resultados | 🟡 status `Pediu` por destinatário | ⬜ | sem agregado; conta pedido criado, não pago |

## 2. Fatos medidos

| # | Fato | Onde |
|---|---|---|
| 1 | Rotas de campanha: `GET/POST api/campanhas`, `GET/PUT {id}`, `cancelar`, `publico`, `destinatarios`, `ondas`; policy **Gerente** | `Api/Controllers/CampanhasController.cs:15-16,31-97` |
| 2 | Sugestão por interesse usa policy **Admin** (diverge do item 1) | `Api/Controllers/CampanhasSugestoesController.cs:11-12,21` |
| 3 | Interesses do cliente: `api/clientes/{id}/interesses`, policy **Admin** | `Api/Controllers/ClienteInteressesController.cs:15-16` |
| 4 | Filtro do público: `Todos`, `TagsIncluir`, `TagsExcluir`, `ComprouItemId`, `ComprouNosUltimosDias` | `Domain/Entities/Campanhas/FiltroCampanha.cs:10-15` |
| 5 | Exclusões com motivo: `restricao`, `limite_semanal`, `sem_consentimento`, `bloqueado`, `sem_telefone`, `cancelada` | `Domain/Entities/Campanhas/MotivoExclusaoCampanha.cs:6-13` |
| 6 | Atribuição: janela fixa de 7 dias, marca `Pediu` **na criação do pedido** | `Application/Services/Campanhas/AtribuicaoPedidoCampanha.cs:12-21`; chamadas em `CriarPedidoAtendimentoUseCase.cs:123`, `IniciarCheckoutGuestUseCase.cs:122,166` |
| 7 | `CampanhaResult` não traz contagem nenhuma (enviados, falhas, pediram) | `Application/UseCases/Campanhas/CampanhasUseCases.cs:8-27` |
| 8 | `TemplateMeta` é texto livre; não há listagem de templates aprovados na Meta (`git grep message_templates` em `*.cs`: 0) | `Domain/Entities/Campanhas/Campanha.cs:41-42` |
| 9 | `CampanhaJob` faz polling de 60 s (configurável) | `Api/BackgroundServices/CampanhaJob.cs:26-27` |
| 10 | Eventos de notificação `CampanhaMarketing = 44`, `CampanhaLembreteEncerramento = 45` | `Domain/Enums/Notifications/TipoEventoNotificacao.cs:65-66` |
| 11 | `Cupom` do SaaS ainda existe, sem `EmpresaId`, com `PlanoId` (a P02 #1135 removeu o billing e deixou o cupom) | `Domain/Entities/Cupom.cs:3-14` |
| 12 | Preço único por item: `PrecoStorefront` ou `Produto.PrecoReferencia`; variação tem preço próprio | `CardapioItem.cs:101,232-238,459` |
| 13 | `PrecoEfetivo()` é o ponto único do preço: checkout, pedido da conversa, menu público e admin | `CheckoutCoreService.cs:206,298`; `ListarCardapioPublicoUseCase.cs:82`; `ListarCardapioAdminUseCase.cs:56`; `ObterCardapioItemAdminUseCase.cs:65` |
| 14 | `Pedido` não tem desconto: `Total` = soma dos itens | `Domain/Entities/Pedido.cs:77,244-252` |
| 15 | Lotes vencendo existem na API: `GET api/producao/lotes?vencendoEmDias=3` | `Api/Controllers/ProducaoController.cs:44-47` |
| 16 | `GET api/clientes` filtra só por `ativo` e `search`, sem tag nem consentimento | `Api/Controllers/ClientesController.cs:42-51` |
| 17 | Evento SSE `cardapio.item_com_interesse` é publicado e o console não escuta (`git grep` no console: 0) | `Application/Ports/Output/Atendimento/EventosOperacao.cs:11` |
| 18 | Protótipo **não tem tela de campanha**; tem Fidelidade (Cupons, Regra, Recompensas, Sorteios) e o cartão do assistente "que tal enviar uma promoção?" | `Console/src/features/gestao/fidelidade/AbaFidelidade.jsx:283,299,306,324`; `Console/src/dominio/assistente.js:76` |
| 19 | Na aba Fidelidade do console, modo API mostra faixa "não ligado (F15)" | `Console/src/features/gestao/ModalGestao.jsx:35-40` |
| 20 | Fonte da promoção de preço: a dona descreve baixar a lasanha "de 85 por 60, por 49 reais" para queimar estoque | `Downloads/01-TRANSCRICAO.md:218` |

## 3. Sub-tela → o que cobre → estado

| Sub-tela do M6 | Backend / S / F que cobre | Estado | Fatia nova |
|---|---|---|---|
| Campanhas: lista e status | S28 (`api/campanhas`) | backend ✅ · tela ⬜ | **M6.1** |
| Campanha: público e exclusões por motivo | S29 (`POST {id}/publico`, `GET destinatarios`) | backend ✅ · tela ⬜ | **M6.1** |
| Campanha: arte, mensagem, template | S28, #1249 (arte) | backend 🟡 (template em texto livre) · tela ⬜ | **M6.1**, **M6.6** |
| Campanha: agenda, ondas, encerramento, lembrete | S30 (`CampanhaJob`, `POST ondas`) | backend ✅ · tela ⬜ | **M6.1** |
| Resultados por campanha e do período | S30 só marca `Pediu` | 🟡 | **M6.2** |
| Promoções (preço com período) | nada | ⬜ | **M6.3**, **M6.4** |
| Queima de estoque (UC-07) | lotes vencendo (fato 15) + S29/S30 | 🟡 peças soltas | **M6.5** |
| Cupons | F15 (`CupomLoja`, `Pedido.Desconto`) | ⬜ (F15 sem PR) | não: F15 |
| Fidelidade: regra, recompensas, extrato, resgate | F15 (`RegraFidelidade`, `LancamentoPontos`, `Recompensa`) | ⬜ (F15 sem PR) | não: F15 |
| Sorteios | F15 deixa fora "além de registrar o número" | ⬜ | não: backlog |
| CRM: lista de clientes por tag, consentimento, compra | S24 tags, S38 consentimento, S29 query de público | backend 🟡 · tela ⬜ | **M6.7** |
| CRM: interesses abertos e aviso de volta | S31 | backend ✅ · console não escuta o SSE | **M6.7** |
| Consentimento por canal na ficha | S38 + F02 | ✅ | não |
| Mensagem programada 1 a 1 | S39 | backend ✅ · tela fora do go-live (decisão 4 do doc 11) | não: é do M3 |
| Respostas prontas e automáticas | S42 + F10 | backend ✅ · console local | não: F10, menu no M7 |

## 4. Proposta de modelo da promoção de preço

A Thati descreve promoção como **preço menor de um prato por alguns dias** (fato 20; US-052 e UC-07 usam a
lasanha). Não há citação de leve X pague Y nem de combo. Proposta (recomendada) e alternativas:

| Opção | Modelo | Cobre a fala dela | Custo | Risco |
|---|---|---|---|---|
| **A (Recomendado)** | `PromocaoItem`: preço promocional por item (ou variação) com `InicioEm` e `FimEm` | sim | M: entidade + `PrecoEfetivo(agora)` em 5 call-sites (R8) | baixo: o preço já é congelado no `PedidoItem` |
| B | Leve X pague Y por item | não citado | G: regra de carrinho, rateio no MP | alto: mexe no cálculo do total e no item de desconto do MP |
| C | Desconto percentual por seção ou linha | parcial | M | médio: seção ainda sem CRUD (M1) |
| D | Só cupom (F15), sem preço promocional | parcial (exige o cliente digitar código) | zero | a dona perde o "de/por" que ela citou; o cliente precisa saber o código |

Regras da opção A (valem para M6.3):
- `PrecoPromocional > 0` e `< preço base vigente` no momento de criar; período com `FimEm > InicioEm`.
- Uma promoção vigente por item (ou variação) por vez: sobreposição devolve 409 com a promoção que conflita.
- O preço vale pelo instante do pedido (`TimeProvider`), fuso `America/Sao_Paulo`; o `PedidoItem` já guarda o
  preço unitário, então encerrar a promoção não muda pedido feito.
- Vínculo opcional `CampanhaId`: a campanha pode usar `{{preco_de}}` e `{{preco_por}}` na mensagem.
- Cupom sobre item em promoção segue a decisão DM6-2.

## 5. Fatias novas

Formato do plano irmão. Tier pelo ADR-0055: baixo salvo migration, RLS, auth, permissão ou policy.
Todas dependem da Fase A (M0.1 tema, M0.2 shell e sala, M0.3 perfis × módulos em [01-fundacao](01-fundacao.md)).

### M6.1 · Tela de campanhas (lista, editor em passos, ondas)

**Problema.** S28 a S30 estão vivas e sem tela (fato 1); a dona não consegue montar US-052 a US-055 sem
alguém chamar a API à mão.
**Abordagem.** Tela do M6 no console, só consumindo rotas existentes. Editor em 4 passos, no jeito do
protótipo (gaveta lateral, botões grandes): **Público** (filtro do fato 4 + tags de restrição) →
**Conteúdo** (mensagem com prévia de `{{nome}}`, arte pelo upload existente, template Meta) → **Agenda**
(disparo, encerramento, lembrete, tamanho da onda) → **Revisão** (resultado do `POST {id}/publico`: total,
pendentes, excluídos por motivo do fato 5, amostra de 10 nomes).
**Escopo.**
- `infra/api/campanhasApi.js` e ações em `aplicacao/api/campanhas.js`; lista com status e próxima ação.
- Botão **Disparar próxima onda** só aparece com `Status = Enviando` e pendentes > 0, com a contagem; nunca
  automático (RN-42).
- Cancelar com confirmação que diz quantos ainda não receberam.
- Aviso (não trava, D7) quando o disparo cai fora da janela de 24 h sem template.
**Fora.** Painel de resultado (M6.2); seletor de template (M6.6); promoção (M6.4).
**Aceite.**
- [ ] Campanha criada no console aparece no `GET api/campanhas` após recarregar, com filtro e agenda.
- [ ] Revisão mostra a mesma contagem por motivo que `POST {id}/publico` devolve.
- [ ] Segunda onda só sai pelo botão; recarregar a tela não dispara nada.
- [ ] Perfil sem acesso ao M6 recebe 403 da API e o card bloqueado na sala (M0.3).
**Testes (Red).** prova Playwright `prova-m6-1-campanhas.mjs` contra API local; testes de unidade do
mapeamento `campanhaDaApi`.
**Rollback.** Revert do squash; backend intocado.
**Depende de.** M0.2, M0.3. **Tamanho.** G. **Tier.** baixo (só console).

### M6.2 · Resultado da campanha e do período

**Problema.** O único resultado é `CampanhaDestinatario.Status = Pediu` (fato 6), marcado quando o pedido é
**criado**; RN-23 diz que o pedido existe antes do pagamento, então pedido abandonado conta como conversão.
Não há agregado (fato 7).
**Abordagem.** Query de leitura no `Postgre/Queries`, com `EmpresaId` no `WHERE` (ADR-0010), juntando
destinatários e pedidos. Conversão conta **pedido pago** dentro da janela (DM6-4); `Pediu` continua sendo
gravado como está, para não mexer na S30.
**Escopo.**
- `GET api/campanhas/{id}/resultado`: público, excluídos por motivo, enfileirados, enviados, falhas, pediram,
  pagaram, receita paga (soma de `Pedido.Total` pago), conversão = pagaram / enviados, quebra por onda.
- `GET api/campanhas/resultados?de=&ate=`: uma linha por campanha do período + totais.
- Tela: cartão de resultado na campanha e aba **Resultados** do M6 com tabela e número grande.
**Fora.** Custo da mensagem na Meta; RFM; comparação com período anterior.
**Aceite.**
- [ ] Campanha com 30 enviados, 5 pedidos criados e 3 pagos mostra pediram 5, pagaram 3, conversão 10 %.
- [ ] Pedido pago 8 dias após o envio não conta (janela de 7 dias, `AtribuicaoPedidoCampanha.cs:12`).
- [ ] Isolamento: resultado de outra empresa volta vazio.
**Testes (Red).** `ResultadoCampanhaQueriesTests.SoPagoConta`, `...ForaDaJanelaNaoConta`, `...IsolamentoDeTenant`.
**Rollback.** Remover query, rotas e aba.
**Depende de.** M6.1. **Tamanho.** M. **Tier.** baixo (leitura, sem migration).

### M6.3 · Promoção de preço por item com período (backend)

**Problema.** Não existe preço promocional (fatos 12 a 14); a fala da dona (fato 20) e o UC-07 pedem.
**Abordagem.** Opção A da seção 4. `PromocaoItem (Id, EmpresaId, CardapioItemId, VariacaoId?,
PrecoPromocional, InicioEm, FimEm, CampanhaId?, Motivo?, CriadaPorUsuarioId, EncerradaEm?)` em
`Domain/Entities/Campanhas/`. O preço vigente passa por um serviço `PrecoVigente` que recebe o instante;
`PrecoEfetivo()` ganha a sobrecarga com as promoções e os 5 call-sites do fato 13 mudam no mesmo commit (R8).
**Escopo.**
- Entidade, configuração EF, migration `AddPromocaoItem` com bloco `DO $rls$`; índice
  `(EmpresaId, CardapioItemId, InicioEm, FimEm)`.
- `GET/POST api/promocoes`, `PUT {id}`, `POST {id}/encerrar` (policy do módulo M6, M0.3).
- Menu público (`ListarCardapioPublicoUseCase.cs:82`) devolve `PrecoOriginalCentavos` e `PromocaoAte` quando
  houver promoção vigente (campo aditivo; o site em outro repositório decide se mostra, DM6-3).
- Variáveis `{{preco_de}}` e `{{preco_por}}` na renderização da campanha quando `CampanhaId` existir.
**Fora.** Leve X pague Y; promoção por seção; combo (M1).
**Aceite.**
- [ ] Item de R$ 85 com promoção de R$ 49 de sexta a domingo: pedido no sábado cobra R$ 49; na segunda, R$ 85.
- [ ] Pedido feito no sábado continua R$ 49 depois que a promoção acaba.
- [ ] Promoção sobreposta no mesmo item devolve 409; preço promocional maior ou igual ao base devolve 422.
- [ ] Mercado Pago recebe o total com o preço promocional (teste de integração do checkout).
- [ ] RLS: `promocao_item` de outra empresa volta vazia.
**Testes (Red).** `PromocaoItemTests.PrecoMaiorQueBaseRecusa`, `PrecoVigenteTests.DentroEForaDoPeriodo`,
`CheckoutCoreServiceTests.UsaPrecoPromocional`, `PromocaoRepositoryTests.IsolamentoDeTenant`.
**Rollback.** Migration `Down`; `PrecoVigente` sem promoções volta ao `PrecoEfetivo()`.
**Depende de.** DM6-1. **Tamanho.** M. **Tier.** alto (migration e checkout).

### M6.4 · Tela de promoções

**Problema.** M6.3 sem tela é a mesma armadilha do fato 1.
**Abordagem.** Aba **Promoções** do M6: lista vigentes, agendadas e encerradas; criar a partir do item do
cardápio mostrando "de R$ X por R$ Y" e o período; atalho **Avisar clientes** abre o M6.1 com
`ComprouItemId` preenchido e a mensagem com `{{preco_por}}`.
**Escopo.** `infra/api/promocoesApi.js`, aba, selo "promoção até dd/mm" no item do cardápio do console.
**Aceite.**
- [ ] Promoção criada aparece no menu público com `PrecoOriginalCentavos`.
- [ ] Encerrar antes do fim devolve o preço base no próximo pedido.
- [ ] "Avisar clientes" abre a campanha com o filtro do item.
**Testes (Red).** prova Playwright `prova-m6-4-promocoes.mjs`.
**Rollback.** Revert do squash.
**Depende de.** M6.3, M6.1. **Tamanho.** M. **Tier.** baixo.

### M6.5 · Queima de estoque (UC-07) a partir do lote vencendo

**Problema.** UC-07 começa com "a dona vê o lote destacado como próximo do vencimento"; a API tem os lotes
(fato 15) e a campanha tem o resto, mas nada liga as duas pontas.
**Abordagem.** Sem entidade nova. No M6, painel **Vencendo** lista `GET api/producao/lotes?vencendoEmDias=N`;
o botão **Fazer campanha** monta um rascunho com `ComprouItemId` do item do cardápio ligado ao produto do
lote, `TamanhoOnda` = saldo do lote em porções, `EncerramentoEm` = validade e, se a dona marcar, uma
promoção (M6.3) com o mesmo período. Rascunho, nunca disparo (D8).
**Escopo.** Tela e um endpoint de leitura `GET api/campanhas/sugestoes/lote/{loteId}` que devolve item do
cardápio, saldo e validade (a ligação lote → `CardapioItem.ProdutoId` precisa ser medida: um produto pode ter
mais de um item no cardápio; **(inferência)**).
**Aceite.**
- [ ] Lote de lasanha com 30 porções vencendo em 2 dias gera rascunho com onda de 30 e encerramento na validade.
- [ ] Nada é enviado sem a dona agendar.
**Testes (Red).** `SugestaoCampanhaLoteUseCaseTests.OndaIgualAoSaldo`, `...ProdutoComDoisItensPedeEscolha`.
**Depende de.** M6.1 (M6.3 opcional). **Tamanho.** M. **Tier.** baixo.

### M6.6 · Templates de marketing aprovados na Meta

**Problema.** Fora da janela de 24 h só template aprovado sai; `TemplateMeta` é texto livre (fato 8): um erro
de digitação vira falha silenciosa no outbox, detectada só depois do disparo.
**Abordagem.** Ler os templates da conta WhatsApp pela Graph API (`GET /{waba-id}/message_templates`,
confirmar campos na doc da Meta antes do Red) com cache curto, e validar o nome e os parâmetros ao agendar
(a S39 exige modelo fora da janela ao agendar; medir se ela confere que o modelo existe e, se não, criar o validador uma vez para as duas, **(inferência)**).
**Escopo.** `GET api/campanhas/templates-meta` (só aprovados, categoria marketing); validação em
`Campanha.Agendar` via porta; seletor no passo Conteúdo do M6.1.
**Aceite.**
- [ ] Agendar com template inexistente devolve 422 com a lista dos aprovados.
- [ ] Seletor mostra só templates `APPROVED` de marketing.
**Testes (Red).** `ValidadorTemplateMetaTests.InexistenteRecusa`, com cliente HTTP falso.
**Depende de.** M6.1; credencial do WhatsApp por F16. **Tamanho.** M. **Tier.** baixo.

### M6.7 · CRM: lista de clientes filtrável e interesses abertos

**Problema.** Tags, consentimento e interesse existem, mas só aparecem cliente a cliente na ficha (fato 16);
o aviso de item que voltou é publicado e ninguém escuta (fato 17).
**Abordagem.** Uma query de leitura que reaproveita os filtros da S29 (`CampanhaPublicoQueries`) para listar,
não para materializar destinatários. Ação **Criar campanha com este filtro** leva o filtro para o M6.1.
**Escopo.**
- `GET api/crm/clientes?tags=&semTags=&comprouItemId=&dias=&consentimento=&canal=&interesseAberto=&page=`.
- Aba **Clientes** do M6 (tabela densa: nome, tags, último pedido, canais com consentimento, interesses).
- Aba **Interesses**: itens com interesse aberto e quantidade; console escuta `cardapio.item_com_interesse`
  e mostra o selo "3 clientes esperando" com atalho para campanha ou aviso manual (nada sai sozinho, D8).
**Fora.** Segmento salvo; RFM; exportar CSV.
**Aceite.**
- [ ] Filtro "comprou lasanha nos últimos 60 dias, sem tag `sem_lactose`" devolve o mesmo total que o
  `POST publico` de uma campanha com o mesmo filtro.
- [ ] Republicar item com interesses mostra o selo no console sem recarregar.
**Testes (Red).** `CrmClientesQueriesTests.MesmoTotalQuePublico`, `...IsolamentoDeTenant`.
**Depende de.** M6.1. **Tamanho.** M. **Tier.** baixo.

## 6. Ordem dentro do M6

```
F15 (cupons e fidelidade, plano irmão) ──────────────────────────────┐
M6.1 campanhas ─► M6.2 resultados                                    ├─► M6 completo
        ├─► M6.6 templates Meta                                      │
        ├─► M6.7 CRM                                                 │
M6.3 promoção (backend, tier alto) ─► M6.4 tela ─► M6.5 queima ──────┘
```

## 7. Decisões pendentes do Felipe

| # | Pergunta | Opções | Bloqueia |
|---|---|---|---|
| DM6-1 | Modelo da promoção de preço | **(a) Preço promocional por item com período (Recomendado)** · (b) Leve X pague Y · (c) Desconto percentual por seção · (d) Só cupom, sem promoção de preço | M6.3 |
| DM6-2 | Cupom sobre item em promoção | **(a) Não acumula: cupom não desconta item em promoção (Recomendado)** · (b) Acumula · (c) Vale o que der o menor total para o cliente | M6.3, F15 |
| DM6-3 | Como o site mostra a promoção | **(a) "De R$ 85 por R$ 49" com a data de fim (Recomendado)** · (b) Só o preço novo · (c) Preço normal no site, promoção só para quem recebeu a campanha (via cupom) | M6.3, site |
| DM6-4 | O que conta como conversão no resultado | **(a) Pedido pago em até 7 dias do envio (Recomendado)** · (b) Pedido criado (como hoje) · (c) Pedido entregue | M6.2 |
| DM6-5 | Nome da entidade de cupom do cliente | **(a) `Cupom`, depois que a poda M0.4 apagar o do SaaS (Recomendado)** · (b) `CupomLoja`, como está no F15 | F15, M0.4 |
| DM6-6 | Quem opera o M6 | **(a) Dona e quem ela liberar no perfil × módulo (Recomendado)** · (b) Só Admin · (c) Gerente para cima, como a policy atual de campanha | M6.1, M0.3 |

## 8. Contradições e divergências achadas

| # | Divergência | Evidência | Encaminhamento |
|---|---|---|---|
| 1 | Policies diferentes no mesmo módulo: campanha **Gerente**, sugestão e interesse **Admin**, F15 propõe **Admin** | fatos 1 a 3; doc 11 §F15 API | unificar pela permissão de módulo da M0.3 |
| 2 | Conversão conta pedido **criado**; F15 dá pontos a partir de **pago** | fato 6; doc 11 §F15 Pontos | M6.2 mede pago (DM6-4) |
| 3 | F15 manda "campanha com preço promocional" para S28-S30, que não a especificam | doc 11 §F15 Fora; doc 06 S28-S30 | M6.3 assume a promoção |
| 4 | A poda P02 (#1135) removeu o billing SaaS e deixou `Cupom` com `PlanoId` | fato 11 | entra na M0.4 (ADR-0056 item 1) |
| 5 | Estudo marca Mensagens como ✅; a dona não tem como montar campanha sem tela | `00-estudo.md` §2; fato 1 | M6.1 |
| 6 | Campanha aceita qualquer nome de template; a S39 valida o envio no agendamento, a campanha não | fato 8; doc 09 §S39 | M6.6 |
