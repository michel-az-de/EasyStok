# M5 Caixa: abertura, pagamentos, sangrias, movimentações, relatórios

> **Atualização de execução, 09/10/2026:** a devolução manual pela Ficha (D3-05) grava uma saída com origem `devolucao_pedido`, ligada ao recebimento e à confirmação de Dona/gerente. O total recebido permanece no dia original; a saída entra no dia do registro, inclusive para pedido cancelado ou consolidado em Venda, sem duplicar a receita. A devolução usa o meio informado pela gerente e exige Caixa do dia sem fechamento. Não pode ser apagada pelo estorno genérico de lançamentos. Evidência e limites na seção 25 do plano de ondas. Estorno online, reconciliação com o provedor, reabertura e correção de devoluções continuam pendentes.

Issue: #1316 · [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · 2026-10-01 · Base: master `b713263a` e branch `feat/console-caixa-1244` (`a7c4c3af`, F14).
**Pré-requisito: F14 (#1244)**, em [11-console-fechamento.md §F14](../atendimento-whatsapp/11-console-fechamento.md#f14--caixa-na-api-real--1244). Este doc **não reescreve a F14**: as fatias M5.x começam onde ela termina.
Legenda: **fato** = lido no `path:linha` citado; **inferência** = dedução não reproduzida, marcada; **medir** = depende de dado de produção.

## 1. Veredito

**O CAIXA TEM UM BACKEND SÓ, MAS QUATRO FÓRMULAS DE SALDO E DOIS ARMAZENAMENTOS.** A F14 liga o console
no `api/caixa`; o que falta para a PWA sair é: venda avulsa pela API, uma fonte única de saldo,
conferência do que está na gaveta, reabertura, relatórios e o extrato. A PWA não fala com `api/caixa`:
ela grava `CashEntry` local e sincroniza, e o servidor promove para `MovimentoCaixa`.

```
 PWA (offline)                     Api                                   Console (F14)
 cashEntries[] ──sync──► CashEntry ──CashEntryLinker──► MovimentoCaixa ◄── POST api/caixa/movimentos
 cashClosings[] (só local)                               FechamentoCaixa ◄── POST api/caixa/fechar
 orders[] entregue ─sync─► Order ──OrderLinker──► Pedido + PedidoPagamento("dinheiro") + Venda
                                                  ▲
 WhatsApp/site/MP ──────────────────────────────► PedidoPagamento (método real)
```

## 2. O que existe (backend, medido)

| Peça | Fato | Onde |
|---|---|---|
| Endpoints do caixa | `GET dia`, `POST abrir`, `GET/POST movimentos` e `GET fechamentos` pedem Operador; `POST fechar` e `POST movimentos/{id}/estornar` pedem Gerente | `Api/Controllers/CaixaController.cs:29,44,61,75,93,111,126` |
| Relatório de fechamentos | paginado com `desde`/`ate`; é o único relatório de caixa da API | `CaixaController.cs:126-139` |
| `MovimentoCaixa` | tipos `abertura`, `fechamento`, `entrada`, `saida`; sangria e despesa são `saida` com `Categoria` livre; `Metodo` fechado em 6 valores; `Origem` web/mobile/api | `Domain/Entities/MovimentoCaixa.cs:5-10,23,29,32,42` |
| `FechamentoCaixa` | snapshot: inicial, vendas, pagamentos de pedido, entradas, saídas, final, observações; sem reabertura | `MovimentoCaixa.cs:109-127` |
| `PedidoPagamento` | método, valor, referência, `PagoEm`; **sem `EmpresaId` nem origem** (a origem vem do `Pedido`) | `Domain/Entities/Pedido.cs:394-411`, `Pedido.cs:82` (`Origem`) |
| Saldo esperado | `inicial + vendas(PDV) + pagamentos de pedido + entradas − saídas`; janela BRT; sessão aberta de dia anterior agrega até hoje | `Application/UseCases/Caixa/CaixaSaldoCalculator.cs:11,71-83,106` |
| Abertura automática | primeiro pagamento de pedido do dia abre o caixa com saldo 0 | `RegistrarPagamentoPedidoUseCase.cs:177-179` |
| Travas | lançar e estornar em dia fechado é recusado; saída exige método e descrição | `RegistrarMovimentoCaixaUseCase` (linhas 41-55), `EstornarMovimentoCaixaUseCase` (27-31) |
| Venda de balcão | compõe cliente, produto novo (entrada de estoque com natureza `Compra`), pedido e pagamento numa transação e **empurra o pedido até `entregue`** | `FinalizarVendaBalcaoUseCase.cs:20-33,63,155,199-210`; rota `PedidosController.cs:83-85` |
| Consumidor da venda de balcão | só o Web (`/pedidos/balcao.json`) | `Web/Services/PedidosService.cs:71`, `Web/Views/Pedidos/Index.cshtml:1550` |
| Caixa esquecido | job diário 07:00 BRT, só avisa no sino in-app, 1 aviso por sessão | `Api/BackgroundServices/CaixaEsquecidoJob.cs:11-12,21-23,33` |
| Extrato PDF | renderer QuestPDF registrado no DI e **sem nenhum consumidor** (ocioso); cor Indigo, hora em "UTC" | `Infra.Async/DependencyInjection/ServiceCollectionExtensions.cs:77`; `Infra.Async/Pdf/FechamentoCaixaExtratoTemplate.cs:61,72` |
| Quick report `caixa-turno` | `api/mobile/reports/quick/caixa-turno`: soma abertura como entrada, soma `Venda` e **ignora `PedidoPagamento`** | `Mobile/Controllers/MobileQuickReportsController.cs:73`; `Infra.Async/Reporting/QuickReports/GetCaixaTurnoQuery.cs:37-55` |
| `vendas-por-canal` | agrupa `Venda` (PDV) por `Canal`, não `Pedido` por `Origem`: WhatsApp e site ficam fora | `AnalyticsController.cs:311`; `Infra.Postgre/Repositories/ReceitaAnalyticsQueries.cs:158-178` |
| Web | `/caixa` e `/caixa/historico` (mesmo backend) + painel `/caixa-mobile` (promover/desligar `CashEntry`) | `Web/Controllers/CaixaController.cs:10-91`; `Web/Controllers/CaixaMobileController.cs:10,22,32` |

### 2.1 Duplicação mobile (CashEntry e promover)

| Fato | Onde |
|---|---|
| A PWA grava `CashEntry` (`income`/`expense`) e envia `cashEntry.upsert` pelo sync | `wwwroot/pwa/sync.js:392`; `Mobile/Services/SyncMutationDispatcher.cs:41,377-397` |
| O servidor promove para `MovimentoCaixa` automaticamente (linker) ou pelo botão "promover" | `Mobile/Services/Linkers/CashEntryLinker.cs:7`; `Mobile/Controllers/MobileCashController.cs:46-75` |
| O pull reverso manda `MovimentoCaixa` para a PWA e traduz **`abertura` como `income`**: a PWA conta o troco inicial como receita | `Mobile/Services/SyncReversePullService.cs:148-152` |
| O fechamento da PWA **fica só no aparelho**: o diff de coleções não inclui `cashClosings` | `sync.js:389-396` |
| O servidor tem `ApplyClosing` que **sobrescreve** o `FechamentoCaixa` do dia, mas a PWA lista `cashClosing.upsert` e o despachante só conhece o prefixo `closing` | `SyncMutationDispatcher.cs:42,402-447`; `sync.js:1756` |
| Pedido da PWA entregue vira `Pedido` + `PedidoPagamento` com método fixo **"dinheiro"** e, pelo mesmo linker, uma `Venda` | `Mobile/Services/Linkers/OrderLinker.cs:186,243,343-363`; `Mobile/Services/MobileSaleSyncService.cs:10-12` |
| A PWA não tem campo de forma de pagamento no pedido (0 ocorrências de `paymentMethod`/`formaPagamento`) | `wwwroot/pwa/index.html` |

**Inferência (forte, não reproduzida):** pedido da PWA entregue entra duas vezes no `saldoEsperado`
(uma como `Venda`, outra como `PedidoPagamento`), porque `CaixaSaldoCalculator.cs:106` soma as duas
fontes. A poda já cita esse débito (#950, #954, #934 em `07-poda.md:69`). A F14 contorna no console com
`SaldoAtendimento` (sem vendas do PDV), mas o card do Dashboard (`AnalyticsRepository.ResumoDia.cs:66`)
continua com a fórmula antiga.

### 2.2 As quatro fórmulas de saldo

| Quem | Fórmula | Onde |
|---|---|---|
| `CaixaSaldoCalculator` (Web `/caixa`, Dashboard, `api/caixa/dia.saldoEsperado`) | inicial + vendas PDV + pagamentos + entradas − saídas | `CaixaSaldoCalculator.cs:106` |
| F14 `SaldoAtendimento` (console) | inicial + pagamentos + entradas − saídas | branch F14, `CaixaSaldoCalculator.cs` (+9 linhas) |
| `caixa-turno` (mobile) | (abertura + entradas + vendas PDV) − saídas | `GetCaixaTurnoQuery.cs:37-57` |
| PWA local | pedidos entregues + income − expense (sem abertura própria; recebe a do servidor como income) | `index.html:13012-13040` |

Nenhuma delas é **o dinheiro que está na gaveta**: todas somam Pix e cartão junto com espécie.

## 3. Protótipo e console (medido)

`EasyStock.Console/src/dominio/caixa.js` e `aplicacao/casos/caixa.js` são **idênticos** aos do protótipo
(`casa-da-baba-atendimento/prototipo-omni` @ `448f980`, `diff -q` vazio).

| Regra do protótipo | Onde | Situação |
|---|---|---|
| Dia = agregação de lançamentos, sem sessão como agregado | `dominio/caixa.js:1-12` | igual ao backend |
| Atalhos de saída: Sangria, Despesa, **Suprimento** | `dominio/caixa.js:23` | **erro de domínio**: suprimento (reforço de troco) é `entrada`; o teste do backend usa assim (`CaixaUseCasesTests.cs:298-299`) |
| Meio da cobrança → método do caixa: maquininha e link de cartão viram crédito, vale vira outro, "ninguém decidiu ainda" | `dominio/caixa.js:35-47` | decisão aberta (D-M5-06) |
| Resumo por método em tempo real | `dominio/caixa.js:221-269` | F14 trouxe `porMetodo` |
| Fechamento com contado e diferença sobre o **saldo total** | `dominio/caixa.js:272-297` | F14 trouxe `ValorContado`/`Diferenca` sobre o total |
| Venda avulsa (UC-11): itens do cardápio, valor nunca digitado, entra na esteira | `aplicacao/casos/caixa.js:6-12`; `features/gestao/caixa/ModalVendaAvulsa.jsx:12-16` | **adiada pela F14** ("a venda avulsa pelo console fica para depois", `changelog.d/1244.md` na branch F14) |

**Estado da F14 no instante da medida:** backend commitado (`a7c4c3af`, lacunas 1, 2, 3 e 5); console
staged e não commitado (11 arquivos, `AbaCaixaApi.jsx` 219 linhas); branch 25 commits atrás do master. A aba da F14 não tem: venda avulsa, reabrir, histórico, relatórios, extrato.

## 4. O que a Thati e o Felipe pedem

| Fonte | Pedido | Vira |
|---|---|---|
| US-034 (`Downloads/02-ESTORIAS-DE-USUARIO.md:969-992`), UC-11 (`03-ANALISE...md:475-489`), RN-26 | venda avulsa entra na mesma esteira e baixa o mesmo estoque; "o caixa existente já é usado hoje e precisa continuar" | M5.2 |
| RN-25 (`03-ANALISE...md:173`) | pagamento confirmado toca o som de caixa | já no SSE do console (F14 mantém) |
| RN-36 (`03-ANALISE...md:194`) | comida não volta, devolve o valor | F14 lacuna 3 (reembolso sai do caixa) |
| Rascunho do Felipe (README §1) | abertura/fechamento, pagamentos, sangrias, movimentações, relatórios | M5.1 a M5.7 |
| Transcrição (`01-TRANSCRICAO.md:281`) | "Esse caixa serve pra alguma coisa ou não? [...] Então eu devia manter o caixa" | **inferência:** o uso real do caixa pela dona é o avulso; relatório por período **não aparece** nas entrevistas, é pedido do Felipe |

## 5. Ordem

```
F14 (#1244, outra sessão) ──► M5.1 tela Hoje no shell ──► M5.2 venda avulsa ──► M5.8 PWA só leitura
M0 shell/sala (01-fundacao) ─┘          │
                                         └─► M5.3 fonte única do saldo ─► M5.4 conferência e reabertura
                                                    │                          │
                                                    ├─► M5.5 pagamentos e movimentações
                                                    └─► M5.6 relatórios ─► M5.7 extrato (PDF, texto, térmica)
 M5.1..M5.8 + paridade da §6 (M2, M3, M4, M7) ──► M5.9 aposentar PWA e espólio mobile (último)
```

## M5.1 · Tela "Hoje" do módulo Caixa · tier baixo · console

**Problema.** A F14 deixa o caixa como aba da Gestão do atendimento. O ADR-0056 item 3 pede módulo com
menu próprio, e a PWA tem coisas que a aba não tem: aviso de dia anterior aberto
(`index.html:11757`), atalho de lançamento manual (`index.html:21687-21765`).

**Abordagem.**
- Mover `AbaCaixaApi` (branch F14) para a rota do M5 com menu lateral: **Hoje · Pagamentos ·
  Movimentações · Fechamentos · Relatórios**. "Hoje" é a tela da F14 mais o que falta abaixo.
- Atalhos de um toque: **Sangria** (`saida`, categoria "Sangria", método dinheiro), **Suprimento**
  (`entrada`, categoria "Suprimento", dinheiro), **Despesa** (`saida`). Corrige `dominio/caixa.js:23`.
- Faixa "Caixa de DD/MM ainda aberto" lida de `AberturaPendenteCrossDay`/`AbertoDesde` do `api/caixa/dia`
  (`CaixaSaldoCalculator.cs:44-47`), com botão para fechar aquele dia (Gerente).
- Sem backend novo.

**Aceite.**
- [ ] Prova `prova-m5-1-hoje.mjs`: Sangria grava `saida`, Suprimento grava `entrada`, os dois com método dinheiro.
- [ ] Com abertura de ontem sem fechamento, a faixa aparece e some depois de fechar.
- [ ] Operador comum vê Fechar e Estornar travados com o motivo (regra da F14 mantida).

**Depende.** F14 mergeada; M0 shell. **Tamanho.** P.

## M5.2 · Venda avulsa pela API (lacuna 4 da F14) · tier baixo · backend + console

**Problema.** UC-11 e RN-26 pedem que a venda de balcão entre na esteira; o backend a empurra direto
para `entregue` (`FinalizarVendaBalcaoUseCase.cs:63,199-210`). A F14 adiou o console.

**Abordagem.**
- Novo modo no `FinalizarVendaBalcaoCommand`: `SeguirEsteira` (padrão `true` para o console). Com ele, o
  pedido nasce pago e para em `Aguardando`; cozinha e entregas o veem como qualquer outro. Sem ele, o
  comportamento atual (o Web `/pedidos/balcao.json` continua igual até a M5.9).
- `Origem = "balcao"` no `Pedido` (campo existe, `Pedido.cs:82`) para o relatório por origem.
- Console: `ModalVendaAvulsa` (itens do cardápio, valor calculado, método) chama `POST api/pedidos/balcao`.
  Cliente opcional pelo dossiê do M3.
- Estoque zerado: avisa e deixa salvar (D7 "avisa, não trava"; UC-11 E1 remete ao UC-09).

**Aceite.**
- [ ] Teste de integração: venda avulsa com `SeguirEsteira` aparece na fila da cozinha (S19) e baixa estoque ao chegar em `pronto`.
- [ ] O pagamento entra em "Pagamentos de pedidos hoje" com o método escolhido.
- [ ] Web `/pedidos/balcao.json` sem a flag continua indo a `entregue` (teste existente verde).

**Depende.** M5.1. **Tamanho.** M.

## M5.3 · Fonte única do saldo · tier baixo · backend

**Problema.** Quatro fórmulas (§2.2), dupla contagem provável do pedido da PWA (§2.1), `caixa-turno`
sem pagamentos. O número do Dashboard, do Web e do console não batem entre si.

**Abordagem.**
- `CaixaSaldoCalculator` passa a devolver o saldo **por método** e o saldo **em dinheiro** (o que está
  na gaveta), além do total. Fórmula escolhida em D-M5-01.
- Não somar `Venda` cujo `Pedido` já tem `PedidoPagamento` (vínculo `Pedido.VendaId`, já existe no
  resultado de `CriarPedidoUseCase.cs:202`). `SaldoAtendimento` da F14 vira o `SaldoEsperado` padrão;
  o campo antigo some só na M5.9 (contrato aditivo).
- `AnalyticsRepository.ResumoDia` e `GetCaixaTurnoQuery` passam a chamar o mesmo calculador.
- Teste de caracterização antes (Red): pedido da PWA entregue conta duas vezes hoje.

**Aceite.**
- [ ] Teste: pedido mobile entregue com `Venda` e `PedidoPagamento` conta uma vez no saldo.
- [ ] Teste: `caixa-turno`, `ResumoDia` e `api/caixa/dia` devolvem o mesmo saldo para o mesmo dia.
- [ ] `saldoDinheiro` = abertura + pagamentos em dinheiro + entradas em dinheiro − saídas em dinheiro.

**Depende.** F14. **Tamanho.** M.

## M5.4 · Conferência da gaveta e reabertura · tier ALTO (migration) · backend + console

**Problema.** O fechamento confere o total (F14 `ValorContado` sobre o saldo inteiro), mas só o dinheiro
pode ser contado na mão; Pix e cartão se conferem no extrato do provedor. A PWA reabre o dia
(`index.html:13171-13190`); a API não tem reabertura e recusa lançamento em dia fechado.
Pagamento que chega depois do fechamento (Pix tardio) não entra no snapshot (**inferência**:
`FechamentoCaixa` é imutável e o dia segue somando `PedidoPagamento` ao vivo).

**Abordagem.**
- `ValorContado` passa a significar **dinheiro contado** e `Diferenca` = contado − `saldoDinheiro`
  (conforme D-M5-01). Demais métodos aparecem no fechamento como "a conferir no extrato".
- `POST api/caixa/fechamentos/{data}/reabrir` (Gerente, motivo obrigatório): campos `ReabertoEm`,
  `ReabertoPorNome`, `MotivoReabertura` em `FechamentoCaixa` (migration aditiva, bloco RLS já existente).
  Fechar de novo gera snapshot novo; o antigo fica no histórico.
- Pagamento depois do fechamento: o dia mostra "entrou depois do fechamento: R$ X" (lido, sem mudar o snapshot).

**Aceite.**
- [ ] Teste de use case: diferença positiva, negativa e zero sobre o dinheiro.
- [ ] Teste: reabrir sem motivo é recusado; operador recebe 403; reabrir e fechar de novo preserva os dois snapshots.
- [ ] Pix pago às 23h depois do fechamento aparece na faixa "entrou depois".

**Depende.** M5.3. **Tamanho.** M. **Tier.** alto: migration e permissão.

## M5.5 · Pagamentos e movimentações · tier baixo · backend + console

**Problema.** O console só vê o dia de hoje. Não há lista por período de pagamentos (com pedido,
cliente, método, origem) nem de lançamentos (sangrias, despesas) com filtro.

**Abordagem.**
- `GET api/caixa/pagamentos?de&ate&metodo&origem` (Operador): `PedidoPagamento` × `Pedido.Origem`
  × cliente, paginado. Pagamento auto-registrado pelo linker mobile (`OrderLinker.cs:362`, observação
  "Auto-registrado pelo F7-A") sai como método **"não informado"** (D-M5-05).
- `GET api/caixa/movimentos?de&ate&tipo&categoria` (o `GET movimentos` atual é só do dia, `CaixaController.cs:75`).
- Telas **Pagamentos** e **Movimentações**: filtros, total do filtro, ação "desfazer recebido na
  entrega" reusa `PedidosCobrancaController.cs:50`; estornar lançamento reusa `movimentos/{id}/estornar`.

**Aceite.**
- [ ] Teste de integração: filtro por origem `whatsapp`, `storefront`, `balcao` e `mobile` soma o mesmo que o relatório da M5.6.
- [ ] Pagamento auto-registrado da PWA aparece como "não informado".

**Depende.** M5.3 (M5.2 para a origem `balcao`). **Tamanho.** M.

## M5.6 · Relatórios do caixa · tier baixo · backend + console

**Problema.** O único relatório é a lista de fechamentos (`CaixaController.cs:126`). `vendas-por-canal`
lê `Venda`, não `Pedido` (`ReceitaAnalyticsQueries.cs:158`), e não enxerga WhatsApp nem site.

**Abordagem.** `GET api/caixa/relatorio?de&ate&agrupar=dia|metodo|origem` sobre a fonte única da M5.3.

| Relatório | Linhas | Colunas |
|---|---|---|
| Por período | um dia por linha | inicial, recebido por pedidos, entradas, saídas, saldo, contado, diferença, fechado por |
| Por método | Pix, dinheiro, crédito, débito, transferência, outro, não informado | quantidade, valor, % |
| Por origem | whatsapp, instagram, messenger, site, balcão, mobile | pedidos pagos, valor, ticket médio |
| Fechamentos com diferença | só dias com diferença ≠ 0 | data, diferença, observação, quem fechou |

- Tela **Relatórios**: período (hoje, 7, 30 dias, mês, livre), quatro abas, exportar CSV.
- O financeiro (contas a pagar e a receber, `FinanceiroController.cs:26` fluxo de caixa) **fica no
  EasyStock.Web** (ADR-0056 item 7); o M5 linka para lá.

**Aceite.**
- [ ] Teste: soma do relatório por método = soma por origem = soma por período, para o mesmo intervalo.
- [ ] CSV abre no Excel com acento e vírgula decimal.

**Depende.** M5.3, M5.5. **Tamanho.** M.

## M5.7 · Extrato do fechamento · tier baixo · backend + console

**Problema.** A PWA compartilha o fechamento em texto e imprime (`index.html:20351,20430,20489`). O
backend tem o PDF pronto e ocioso (`ServiceCollectionExtensions.cs:77`), com cor Indigo e hora em UTC
(`FechamentoCaixaExtratoTemplate.cs:61,72`).

**Abordagem.**
- `GET api/caixa/fechamentos/{data}/extrato.pdf` (Operador) monta `FechamentoCaixaExtratoPdfData` e chama
  o renderer existente. Template veste a marca (Lora/Nunito, Cacau/Caramelo, design-system-v1) e mostra BRT.
- Texto curto para WhatsApp (copiar) e impressão térmica 58 mm como **imagem preta** pelo caminho dos
  impressos (S49/S52, `docs/plan/atendimento-whatsapp/12-impressos.md`), sem CPF.

**Aceite.**
- [ ] Teste de smoke do PDF com o template novo (estende `FechamentoCaixaExtratoRendererSmokeTests`).
- [ ] Hora do fechamento em BRT; validação visual do Felipe na térmica.

**Depende.** M5.4. **Tamanho.** P.

## M5.8 · PWA: caixa só leitura na convivência · tier baixo · PWA

**Problema.** Enquanto PWA e console coexistem, dois caixas escrevem no mesmo dia: a PWA fecha só no
aparelho, conta a abertura como receita (§2.1) e reabre sem trilha no servidor. A dona veria dois saldos.

**Abordagem.**
- Assim que M5.1 e M5.2 estiverem em produção: a tela Caixa da PWA (`index.html:7662`) vira leitura,
  com o botão "Abrir o caixa no console". Lançamento manual e fechamento somem da PWA.
- **Medir antes** (script do Felipe na VPS): mutações `cashEntry.*` por dia nos últimos 30 dias e
  aparelhos ativos (`MobileDevice`), para saber quem ainda usa a PWA e para quê.

**Aceite.**
- [ ] Na PWA, Caixa não lança nem fecha; o link abre o M5.
- [ ] Contagem de uso medida e anotada na issue antes do merge.

**Depende.** M5.1, M5.2; D-M5-02. **Tamanho.** P.

## M5.9 · Aposentar a PWA e o espólio mobile · tier ALTO · Api + Web + infra

Só abre quando **toda** linha da matriz da §6 estiver ✅ ou "morre" decidida, e a medida da M5.8
mostrar zero mutação por 14 dias seguidos.

| Remove | Medida | Onde |
|---|---|---|
| PWA | `index.html` 22.989 linhas, 950 KB, mais `sync.js` (110 KB), `queue-store.js`, `sw.js`, `photo-store.js`, `upload-photos.js`, `push-subscribe.js`, `qrcode.min.js`, `checksum-worker.js`, `etiqueta/` | `Api/wwwroot/pwa/` |
| Redirect | `{$CASA_HOST}` com `redir @root /pwa/`; a raiz passa a abrir o console | `Caddyfile:32-37` |
| `Api/Mobile` | 16 controllers, 58 endpoints, 36 arquivos, 6.730 linhas | `Api/Mobile/**` |
| Painéis do Web que consomem `api/mobile` | `/caixa-mobile`, `/clientes-mobile`, `/lotes-mobile`, `/produtos-mobile` (+ divergências), `/pedidos-mobile` | `Web/Controllers/*MobileController.cs`, `Web/Services/{Caixa,Clientes,Lotes,MobileProducts,Pedidos}Service.cs`, `Web/Views/*Mobile/` |
| Caixa do Web | `/caixa`, `/caixa/historico` (conforme D-M5-04) | `Web/Controllers/CaixaController.cs` |
| Fórmula antiga | `caixa-turno`, campo `saldoEsperado` antigo, soma de `Venda` no saldo | `GetCaixaTurnoQuery.cs`, `CaixaSaldoCalculator.cs:106` |

**Acoplamentos que precisam sair antes (medido).** `Pedido.MobileOrderId` (`CriarPedidoUseCase.cs:26,100`;
`PedidoResult.cs:17`; `IPedidoRepository.FindByMobileOrderIdAsync`); `OperacaoCriterios.cs:2,19,51`
(`MobileDevice`); `Infra.Postgre/Hosting/MobileAlertService.cs`; `Worker/BackgroundServices/AgendamentoNotificacaoService.cs:13`
lê `mobile_orders` (contradição 7 do estudo: pedido do site e do WhatsApp ficam sem lembrete).
**Quem mais usa `api/mobile`:** só a PWA e os 5 painéis do Web; o console não chama nenhuma rota
`api/mobile` (grep vazio em `EasyStock.Console/src`). Etiquetas usam `api/etiquetas` e `api/lotes`, que
**não** são mobile e ficam.

**Abordagem.** Duas PRs: (1) código: remove PWA, redirect, controllers, linkers, painéis, desacopla os 4
pontos acima (lembrete passa a ler `Pedido`); (2) migration `RemoverTabelasMobile` **só depois de uma
release sem o código** (regra da P05/P06, `07-poda.md:86`), `Down` recria vazias.

**Aceite.**
- [ ] `git grep -l "Entities.Mobile"` vazio fora de migrations; `git grep "api/mobile"` vazio.
- [ ] `CASA_HOST/` abre o console; `CASA_HOST/pwa/` devolve 404 (validação do Felipe na VPS).
- [ ] Lembrete de pedido agendado dispara para pedido do WhatsApp (teste de integração).
- [ ] Bugs #950, #954, #934 fechados como "removido".

**Depende.** tudo acima + paridade da §6. **Tamanho.** G. **Tier.** alto.

## 6. Matriz de paridade da PWA

Fonte: roteador `go()` (`index.html:10260-10294`), telas `index.html:7325-7879`, menu `8771-8791`,
navegação `8798-8814`. A PWA autentica por aparelho (`X-Device-Id` + `X-Mobile-Api-Key`, `sync.js:171-173`);
a calculadora usa `Bearer` (`index.html:17359-17363`).
Estado no destino: ✅ existe no console · 🟡 parcial · ⬜ falta · ✖ morre.

| # | Função da PWA | Onde | Endpoint que chama | Destino | Estado |
|---|---|---|---|---|---|
| 1 | Início: card do caixa, resumo, insights | `index.html:10572,10817,11445` | estado local (sync) | M0 Início + M5 Hoje | ⬜ |
| 2 | Início: validade, checklist, compras | `index.html:11604,11631,11693` | local | M2 Produção | ⬜ |
| 3 | Pedidos · Novo (cliente, itens, conferir insumos) | `index.html:7563` | `api/mobile/sync` (`order.upsert`) | M3 (pedido na conversa) + M5.2 (avulso) | 🟡 M3 ✅, avulso ⬜ |
| 4 | Pedidos · Andamento (kanban) | `index.html:14027` (`renderOrdersFeed`, kanban) | sync + `operation/stream` | M4 Cozinha | ✅ (S19) |
| 5 | Produção · catálogo, registrar lote, cadastrar produto | `index.html:7441,14559,18148` | sync (`batch`, `product`), `api/mobile/batches/{id}/photos` | M2 (lote) + M1 (produto) | 🟡 lote no console, produto ⬜ |
| 6 | Produção · calculadora (receitas, cesta, criar compra) | `index.html:17385-17664` | `api/mobile/calculadora/*` (5) | M2 planejamento | ⬜ (estudo: "calculadora só na rota mobile") |
| 7 | Conferência (scanner de pedido e lote) | `index.html:7772,17012` | sync | M4 expedição / M2 | ⬜ **decidir** |
| 8 | Caixa: lançar, fechar com conferido, reabrir, compartilhar, imprimir, dias abertos | `index.html:7662,11757,13125,13171,20351,20489,21765` | sync (`cashEntry.upsert`) | **M5** | ⬜ até M5.1 a M5.7 |
| 9 | Finalizados (entregues, cancelados, 7/30 dias) | `index.html:7691,13268` | sync | M3 histórico | ✅ |
| 10 | Clientes (recorrentes, sumidos, abrir WhatsApp) | `index.html:7716,13470` | sync | M3 clientes / M6 CRM | ✅ dossiê; "sumidos" 🟡 |
| 11 | Estoque (saldo, movimentações, entradas, saídas) | `index.html:7879,12575` | sync, `api/mobile/estoque/buscar` | **EasyStock.Web** (bastidor) | ✅ no Web |
| 12 | Compras (o que falta, compartilhar) | `index.html:7807,11583` | local | **EasyStock.Web** compras | ✅ no Web |
| 13 | Histórico (auditoria local do aparelho) | `index.html:7849,12808` | local (`logAction`) | morre | ✖ |
| 14 | Etiquetas (modelos, impressão) | `pwa/etiqueta/imprimir.js:157-232` | `api/etiquetas/templates`, `api/lotes/{id}/etiquetas/render` | **EasyStock.Web** etiquetas | ✅ no Web |
| 15 | Ajustes: loja, impressora Bluetooth, tema | `index.html:14532` | local | M7 (impressão, parâmetros) | 🟡 |
| 16 | Backup do aparelho | `index.html:9982`; `sync.js` | `api/mobile/devices/me/backup` | morre | ✖ |
| 17 | Suporte (chamado com diagnóstico) | `index.html:7736,15947` | `POST api/mobile/tickets` (`sync.js:2271`): **nenhuma rota com "ticket" existe** | morre (já quebrado) | ✖ |
| 18 | Diagnóstico, heartbeat, trace de erro | `sync.js:1662,1831`; `index.html:15325` | `api/mobile/diagnostics/*`, `diag/trace` | morre | ✖ |
| 19 | Parear aparelho, trocar de loja | `sync.js` | `api/mobile/devices/{pair,pair-auto,me/lojas-disponiveis,me/switch-loja}` | morre (login do M0, tenant fixo) | ✖ |
| 20 | Comandos remotos e SSE | `sync.js:1406-1582` | `api/mobile/operation/{stream,sse-token,pending-commands}` | morre (console tem SSE próprio) | ✖ |
| 21 | Versão e atualização | `sw.js`; `index.html` | `api/mobile/version` | morre | ✖ |
| 22 | Notificação push | `push-subscribe.js` | `api/pwa/push/*` (`PwaPushController.cs:15`) | **decidir** (D-M5-07) | ⬜ |
| 23 | Funciona sem internet (fila local) | `queue-store.js`, `sync.js` | sync | **decidir** (D-M5-03) | ⬜ |

**Resumo:** 23 funções · 8 morrem · 4 ficam no Web · 11 vão para o console (3 ✅, 4 🟡, 4 ⬜ fora do M5,
mais o caixa). Linhas 2, 5, 6 e 7 são do M2 e travam a M5.9 tanto quanto o caixa.

## 7. Contradições encontradas

| # | Contradição | Fontes |
|---|---|---|
| 1 | A F14 diz que o console usa "o caixa do EasyStok que a PWA já usa (`api/caixa`)". A PWA não chama `api/caixa`: escreve `CashEntry` pelo sync | branch F14 `infra/api/caixaApi.js:3`; `sync.js:392`; grep sem `api/caixa` na PWA |
| 2 | Quatro fórmulas de saldo, nenhuma mede a gaveta | §2.2 |
| 3 | Suprimento listado como saída no protótipo; é entrada no backend | `dominio/caixa.js:23`; `CaixaUseCasesTests.cs:298-299` |
| 4 | Pull reverso traduz `abertura` como receita para a PWA | `SyncReversePullService.cs:151` |
| 5 | PWA declara `cashClosing.upsert` crítico; o servidor só aceita o prefixo `closing` e lança "Entidade desconhecida" | `sync.js:1756`; `SyncMutationDispatcher.cs:42,44` |
| 6 | `ApplyClosing` sobrescreve o fechamento do dia feito no Web/console, se um dia receber a mutação | `SyncMutationDispatcher.cs:411-447` |
| 7 | Pedido da PWA vira pagamento "dinheiro" sem a PWA ter forma de pagamento; relatório por método fica falso | `OrderLinker.cs:355-363`; `index.html` sem `paymentMethod` |
| 8 | `07-poda.md:66` mede `Api/Mobile` com 22 controllers e 66 endpoints; hoje são 16 e 58 | `07-poda.md:66`; `Api/Mobile/Controllers/` |
| 9 | ADR-0056 e a poda chamam a PWA de "uso diário"; a transcrição sugere que a dona usa pouco o caixa. Não medido em produção | `07-poda.md:69`; `01-TRANSCRICAO.md:281`; M5.8 mede |
| 10 | Extrato PDF pronto desde a #642 e nunca ligado; data em UTC | `ServiceCollectionExtensions.cs:77`; `FechamentoCaixaExtratoTemplate.cs:72` |

## 8. Decisões pendentes do Felipe

**D-M5-01 · O que o fechamento confere?** (bloqueia M5.3, M5.4)
- (a) **(Recomendado)** Só o dinheiro da gaveta: contado × (abertura + dinheiro recebido + entradas em dinheiro − saídas em dinheiro). Pix e cartão aparecem para conferir no extrato do provedor.
- (b) O total de tudo, como no protótipo e na F14.
- (c) Os dois: dinheiro contado e total informativo, diferença só do dinheiro.

**D-M5-02 · O que acontece com o caixa da PWA na transição?** (bloqueia M5.8)
- (a) **(Recomendado)** Vira só leitura assim que M5.1 e M5.2 entrarem em produção, com link para o console.
- (b) Os dois caixas convivem até a M5.9.
- (c) Desliga a PWA inteira no dia em que o M5 entrar.

**D-M5-03 · E sem internet?** (bloqueia M5.9)
- (a) **(Recomendado)** Console é online; queda de internet vira anotação e lançamento depois (venda avulsa com a hora real).
- (b) Fila offline só para venda avulsa e lançamentos de caixa no console.
- (c) Mantém a PWA como contingência offline por tempo indeterminado.

**D-M5-04 · O `/caixa` do EasyStock.Web fica?** (bloqueia M5.9)
- (a) **(Recomendado)** Sai na M5.9; financeiro (contas a pagar e a receber, fluxo de caixa) continua no Web.
- (b) Fica como bastidor, só leitura.
- (c) Fica como está.

**D-M5-05 · Pagamentos antigos da PWA gravados como "dinheiro" sem ninguém escolher.** (bloqueia M5.5, M5.6)
- (a) **(Recomendado)** Relatório mostra "não informado" para os auto-registrados; daqui pra frente o método é obrigatório.
- (b) Aceita como dinheiro.
- (c) A dona corrige um a um numa tela de revisão.

**D-M5-06 · Maquininha e vale-refeição caem em qual método?** (bloqueia M5.6)
- (a) **(Recomendado)** Maquininha pergunta crédito ou débito na hora; vale-refeição vira "outro" com categoria "vale".
- (b) Maquininha sempre crédito, vale sempre outro (tabela atual do protótipo).
- (c) Criar o método "vale" no enum do caixa (mexe em contrato).

**D-M5-07 · Notificação push da PWA.** (bloqueia M5.9)
- (a) **(Recomendado)** Morre; o console avisa por som e SSE com a tela aberta.
- (b) Portar o push para o console (nova permissão do navegador por aparelho).

**D-M5-08 · Reabrir fechamento.** (bloqueia M5.4)
- (a) **(Recomendado)** Gerente reabre com motivo, fica registrado, e o fechamento novo guarda o antigo.
- (b) Não reabre: correção vira lançamento no dia seguinte com referência ao dia errado.
