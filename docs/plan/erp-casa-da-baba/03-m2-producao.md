# M2 · Produção

Issue: #1316 · Plano: [README](README.md) · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md)
Base medida: master `b713263a` (worktree da #1316), 01/10/2026. Leitura somente.
Telas do rascunho do Felipe: **insumos, fichas técnicas, planejamento, produção do dia, controle de perdas**.
Este módulo também leva ao console o estoque que a Thati usa no dia (S22 e S23 do plano do atendimento).

Fontes do jeito de operar: aba "Produção e cardápio" da Gestão
(`EasyStock.Console/src/features/gestao/producao/AbaProducao.jsx`, igual ao protótipo), domínio
`EasyStock.Console/src/dominio/producao.js` (idêntico ao do protótipo), entrevista `Downloads/02-ESTORIAS-DE-USUARIO.md`
(E9, US-060 a US-067) e `03-ANALISE-DIRETRIZES-CASOS-DE-USO.md` (RN-44 a RN-51, UC-08, UC-09).

## 1. Veredito

**O BACKEND DE PRODUÇÃO EM PORÇÕES ESTÁ PRONTO (S23); O RESTO É PEÇA SOLTA SEM TELA.** A produção cria lote,
etiquetas e entrada de estoque numa transação, mas **não baixa insumo pela receita**. A receita (BOM) tem API e
nenhuma tela em front nenhum. A calculadora só existe na rota mobile, usada pela PWA. Perda é só uma natureza de
saída, contada num KPI. No console a aba inteira é demonstração: "Produção e cardápio ainda não estão ligados ao
EasyStok nesta versão (F11)" (`EasyStock.Console/src/features/gestao/ModalGestao.jsx:36`).

| Tela | Backend | Console | Web / PWA (legado) | Fatia |
|---|---|---|---|---|
| Saldo em porções, lotes, desacerto | ✅ S22, S23 | 🟡 demonstração | ✅ Web `Lotes`, `Estoque` | M2.1 |
| Produção do dia | ✅ sem peso real, sem baixa de insumo | 🟡 demonstração | 🟡 PWA | M2.2 |
| Insumos | 🟡 `Produto.EhInsumo` sem filtro na API | 🟡 marcação | 🟡 Web `Produtos` | M2.3 |
| Fichas técnicas (receitas) | ✅ API de composição | ⬜ | ⬜ nenhuma tela | M2.4 |
| Planejamento | 🟡 só `api/mobile/calculadora` | ⬜ | 🟡 PWA | M2.5 |
| Perdas | 🟡 natureza de saída, sem motivo nem relatório | 🟡 ajuste com motivo | 🟡 Web `Saidas` | M2.6 |
| Embalagens | ⬜ necessidade nova | ⬜ | ⬜ | M2.7 |

## 2. Fronteira com o EasyStock.Web (ADR-0056 item 7)

Regra proposta: **o que a Thati faz no dia de produção vai para o console; cadastro pesado, compra e papelada ficam
no Web.** O console mostra o estoque em porções e age sobre ele (produzir, ajustar, perder); o Web continua dono da
gestão completa do inventário.

| Função | Fica no Web (bastidor) | Vai para o console (M2) |
|---|---|---|
| Entradas de compra, reposição, nota de entrada | `EntradasController.cs` (Nova, Reposicao, Historico) | nada |
| Fornecedores, listas de compras, pedidos de compra | `FornecedoresController.cs`, `ListasComprasController.cs` | só o atalho "gerar lista" do planejamento (M2.5) |
| Etiquetas: editor de modelos | `EtiquetasController.cs` (Modelos, Editor) | imprimir etiquetas do lote recém-produzido (M2.2) |
| Lotes: detalhe, peso pendente, impressão | `LotesController.cs` (Detail, AtualizarPeso, Imprimir) | lista de lotes ativos e vencendo (M2.1) |
| Estoque: visão por item, CSV, saída rápida | `EstoqueController.cs` (Index, ExportarCsv, QuickSaida) | saldo em porções por item do cardápio (M2.1) |
| Saídas: histórico e estorno | `SaidasController.cs` (Historico, Estornar) | registrar perda do dia (M2.6) |
| Categorias de estoque, cadastro completo de produto | `CategoriasController.cs`, `ProdutosController.cs` | cadastro rápido de insumo (M2.3) |
| Legado MAUI | `LotesMobileController.cs`, `MobileProductsController.cs` | nada (candidatos à poda: o MAUI saiu em P05) |

## 3. Fatias

### M2.1 · Estoque do dia no console: saldo em porções, lotes, FIFO e desacerto

**Fato medido.** S22 e S23 entregaram a regra que a Thati pediu (`docs/plan/atendimento-whatsapp/04-estoque-minimo.md`):
venda sem saldo **não trava** (`PedidoEstoqueOptions.PermiteEstoqueNegativo = true`,
`PedidoEstoqueIntegrationService.cs:18`); a falta vira `ItemEstoque.QuantidadeDescoberta` (`ItemEstoque.cs:37`);
o ajuste rápido zera o descoberto e exige motivo (`AjustarSaldoRapidoUseCase.cs:46-48`). A baixa acontece quando o
pedido chega a Pronto, Saiu para entrega ou Entregue (`PedidoStateMachine.cs:90-91`) e devolve no cancelamento.

| Peça | Backend | Onde |
|---|---|---|
| Lista de desacertos | `GET api/estoque/desacertos` (permissão `GerenciarEstoque`) | `EstoqueDesacertosController.cs:17,26,30` |
| Ajustar contagem | `POST api/estoque/desacertos/{produtoId}/ajustar` | `EstoqueDesacertosController.cs:40` |
| Lotes vencendo (RN-51) | `GET api/producao/lotes?vencendoEmDias=3` | `ProducaoController.cs:44` |
| Saldo por produto | `GET api/estoque/por-produto/{produtoId}` | `ItemEstoqueController.cs:198` |
| Saldo no cardápio | `EstoqueAtual` = lotes com saldo e não vencidos | `CardapioItemPublicoDto.cs:18-20` |
| Baixa FIFO | ordena por validade | `PedidoEstoqueIntegrationService.cs:80` |

**Lacunas.**

| # | Lacuna | Protótipo | Backend hoje | Correção |
|---|---|---|---|---|
| 1 | Alerta em texto (RN-49) | "Vendeu 2 Lasanha clássica 800 g sem produção lançada." (`dominio/producao.js:182-184`) | lista numérica | texto montado na API, igual ao do protótipo |
| 2 | FIFO por data de produção (RN-50) | lote mais antigo (`dominio/producao.js:82-83`) | por `ValidadeEm` | igual quando a validade em dias é a mesma (inferência); manter por validade e escrever a regra na RN |
| 3 | Visão por item do cardápio | "Saldo por item, a partir dos lotes" (`AbaProducao.jsx:299`) | saldo por produto | endpoint agregado por `CardapioItem` vinculado (lotes, porções, descoberto, vencendo) |
| 4 | Item avulso não tem saldo | nada | sem `ProdutoId`, a baixa ignora (`PedidoEstoqueIntegrationService.cs:64`) | M1 modelo A: item que a casa produz é vinculado |

**Console.** M2 › **Estoque do dia**: as três seções da `AbaProducao` (alertas de produção, saldo por item,
lotes ativos) lendo a API. No balcão, o "ajustar saldo" do `PainelCardapio` chama o mesmo ajuste.
Absorve a parte "saldo e alerta de desacerto" da F11 (#1241).

**Decisão já tomada.** Avisa, não trava (D7, RN-48, US-063); o alerta fica aberto até ela contar (UC-09).

**Aceite.**
- [ ] Venda acima do saldo aparece como alerta em texto; "Ajustar" com contagem e motivo fecha o alerta e persiste.
- [ ] Lote a 1 dia do vencimento aparece destacado com a quantidade restante (US-065).
- [ ] Perfil sem `GerenciarEstoque` vê a seção bloqueada; a API devolve 403.

**Fora.** Contagem completa de inventário (#704, Web); histórico de ajustes (Web).

### M2.2 · Produção do dia

**Fato medido.** `POST api/producao` (`ProducaoController.cs:15,27`) recebe por item produto, porções, peso por
porção, validade em dias e custo (`RegistrarProducaoUseCase.cs:8-13`) e, numa transação sem retry, cria o lote,
finaliza (etiquetas) e dá entrada `Natureza=Producao` (`:100-149`). **Confirmado: não baixa insumo.** O use case
não lê `ProdutoComposicao`; o custo vem do comando ou de `Produto.CustoReferencia` (`:125`). A entrada grava
`ProdutoVariacaoId: null` (`:130`). `LoteItem` guarda quantidade, peso por porção e validade
(`Lote.cs:90-96`), sem peso real total nem destino.

**Lacunas.**

| # | Lacuna | Protótipo / entrevista | Backend hoje | Correção |
|---|---|---|---|---|
| 1 | Peso real ≠ porção (RN-45) | `pesoRealG` e `sobraG` (`dominio/producao.js:38-63`); "572 g reais atendem uma porção de 500" (US-061) | só peso por porção | `PesoRealG` no item do lote; sobra calculada |
| 2 | Destino da porção | comer agora × congelado (`dominio/producao.js:21-24`) | não existe | usar a linha do item do cardápio (RN-17) e não criar enum novo (ver D-M2-04) |
| 3 | Baixa de insumo pela receita | US-067 é "Could"; Felipe: insumo cru é "quase um sistema de logística" | não baixa | opcional por produto, decide D-M2-01 |
| 4 | Porção com saldo próprio | nada | variação nula | segue D-M1-03 do [M1](02-m1-cardapio.md) |
| 5 | Lançar sem ERP na cabeça | ela informa prato, peso, porções, validade | exige `ProdutoId` | console escolhe pelo item do cardápio e resolve o produto |

**Console.** M2 › **Produção do dia**: o `FormularioProducao` (`AbaProducao.jsx:42`) com peso real, identificador,
validade e linhas de porção; ao confirmar mostra o código do lote e o botão "Imprimir etiquetas" (rota de
etiquetas existente, `LotesController.cs:136`). Envia `Idempotency-Key` (a rota está na lista, `ProducaoController.cs:6-9`).

**Decisão já tomada.** Produção sempre gera lote com identificador, data e validade (RN-44); registrada em porções
(RN-46); a lasanha produzida passa a contar na disponibilidade (UC-08 pós-condição).

**Aceite.**
- [ ] Produção de 1.000 g de ravióli em 2 porções de 500 g gera saldo 2 e sobra 0 (US-061); 1.144 g gera sobra 144 g.
- [ ] Repetir o POST com a mesma chave não cria lote novo.
- [ ] Com D-M2-01 = baixa pela receita: insumos descem na mesma transação e falta de insumo **avisa, não trava**.
- [ ] Migration aditiva com teste; tier **alto**.

**Fora.** Montagem de lasanha a partir de insumo intermediário como tela própria (US-067, entra pela receita).

### M2.3 · Insumos

**Fato medido.** Insumo é um `Produto` com `EhInsumo = true`, só filtro de UI (`Produto.cs:38-39`), com unidade
base e rendimento (`:42-48`). `GET api/produtos` não filtra por insumo (`ProdutoController.cs:32-43`); a calculadora
tem `produtos-com-receita` (`MobileCalculadoraController.cs:120`). Insumo intermediário entra pela produção como
qualquer produto (`04-estoque-minimo.md:40`). Mínimo e crítico já existem (`Produto.cs:33-34`,
`PATCH api/produtos/{id}/limiar`, `ProdutoController.cs:143`).

**Proposta.** Nível do controle é o **intermediário** (molho, recheio, massa laminada) mais **embalagem** (M2.7);
farinha e ovo ficam fora, como o Felipe classificou (US-067). Filtro `ehInsumo` em `GET api/produtos`; cadastro
rápido de insumo (nome, unidade, mínimo, custo) no console.

**Console.** M2 › **Insumos**: lista com saldo, unidade, mínimo e "onde é usado" (`GET api/produtos/{id}/composicao/onde-usado`,
`ProdutoComposicaoController.cs:78`).

**Aceite.**
- [ ] Filtro devolve só insumos da empresa (teste de isolamento).
- [ ] Insumo abaixo do mínimo aparece em "Comprar" (alimenta M2.5 e US-066).

**Fora.** Insumo cru (farinha, ovo); fornecedor por insumo (Web).

### M2.4 · Fichas técnicas (receitas)

**Fato medido.** `ProdutoComposicao` é a linha da receita: insumo, quantidade por unidade-base, unidade, override
por loja (`ProdutoComposicao.cs:3-27`). `GerenciarComposicaoUseCase` substitui a receita inteira numa transação,
valida ciclo e tenant e grava o diff de auditoria (`GerenciarComposicaoUseCase.cs:5-11`). Rotas `GET`/`PUT
api/produtos/{id}/composicao` e `onde-usado` (`ProdutoComposicaoController.cs:10,19,37,78`). **Nenhuma tela grava
receita**: o Web não tem (busca por `Composicao` no Web só acha o "caixa composição" do Dashboard) e a PWA só lê
pela calculadora. O `PUT` aceita qualquer usuário autenticado: só `[Authorize]` na classe, sem policy
(`ProdutoComposicaoController.cs:7,37`).

**Lacunas.** Tela de receita; policy no `PUT` (Gerente, ou permissão do módulo M2 via M0.3, tier **alto**);
custo da receita exposto (o núcleo já estima custo, `CalculoProducaoCore.cs:17`); nome: na API "ficha técnica" é a
**nutricional** (`ProdutoController.cs:339`, `ProdutoFichaTecnica.cs:10-24`).

**Console.** M2 › **Receitas**: por prato, rendimento ("rende 6 porções de 500 g"), linhas de insumo com
quantidade e unidade, custo por porção calculado. Rótulo na tela: "Receita"; "Ficha nutricional" fica no Web.

**Decisão já tomada.** Receita de 1 nível; a recursão ficou para a "onda 2" (`CalculoProducaoCore.cs:6-10`).

**Aceite.**
- [ ] Criar, editar e salvar a receita pelo console; o diff aparece na auditoria.
- [ ] Usuário sem permissão do M2 recebe 403 no `PUT` (teste de integração).
- [ ] Custo por porção = soma das linhas × custo do insumo ÷ rendimento (teste com unidade convertida g → kg).

**Fora.** Receita por loja (a Casa da Baba tem uma cozinha); receita de vários níveis.

### M2.5 · Planejamento da produção

**Fato medido.** `CalcularProducaoUseCase` simula o consumo para uma quantidade-alvo, compara com o saldo e marca
falta, sem mexer no estoque (`CalcularProducaoUseCase.cs:3-7`); há versão em cesta e a ponte para compra
(`MobileCalculadoraController.cs:29,46,70,90`). Tudo está só em `api/mobile/calculadora` (`:19`), chamado pela
PWA (`EasyStock.Api/wwwroot/pwa/index.html:17385,17501,17625,17663,17958`). Pedidos agendados têm janela e data
(`CheckoutCoreService.cs:142`), o que dá a demanda dos próximos dias.

**Proposta.** `GET api/producao/sugestao?ate=` monta a lista do próximo dia de produção: por prato,
`max(mínimo − saldo, 0)` (US-066) + porções dos pedidos agendados ainda sem lote + descoberto aberto. `POST
api/producao/planejamento` reaproveita `CalcularCestaProducaoUseCase` e devolve insumos necessários e faltas.
O botão "Gerar lista de compras" usa o `criar-compra` existente e abre a lista no Web.

> **Como ficou (#1502):** as rotas são `GET api/atendimento/producao/sugestao?ate=` e `POST .../planejamento`. A conta
> é `max(mínimo + agendados + descoberto − saldo, 0)`: com saldo acima do mínimo, o saldo cobre os pedidos. A
> lista de compras é gravada pelo `POST api/listas-compras/gerar` (faltas mais o que repõe o mínimo), sem o Web.

**Console.** M2 › **Planejamento**: sugestão editável (ela muda as quantidades), insumos e faltas, "Lançar como
produção" leva as quantidades para o M2.2.

**Aceite.**
- [ ] Teste do use case: mínimo 5, saldo 4, 2 pedidos agendados para amanhã → sugere 3 porções.
- [ ] Rota web com a mesma regra da mobile (um use case, duas rotas) e teste de paridade.
- [ ] A calculadora mobile continua viva até a PWA sair (P05).

**Fora.** Previsão por histórico de vendas; agenda de dias de produção.

### M2.6 · Controle de perdas

**Fato medido.** Perda é uma natureza de movimentação: `Perda`, `Prejuizo`, `Vencimento`, `Doacao`, `UsoInterno`
(`NaturezaMovimentacaoEstoque.cs:8-13`). Registra-se por `POST api/estoque/saida` (`ItemEstoqueController.cs:186`)
com `Natureza` e `Observacoes` opcional (`RegistrarSaidaEstoqueUseCase.cs:61-70`). `MovimentacaoEstoque` não tem
campo de motivo de perda, só `Descricao` e `MotivoEstorno` (`MovimentacaoEstoque.cs:22,38`). O único relatório é
uma contagem que soma Perda, Prejuízo, Ajuste e Vencimento num card só (`MovimentacaoEstoqueRepository.cs:94-100`).
O protótipo só tem ajuste de contagem com motivo (`AbaProducao.jsx:178-206`). A entrevista não fala de perda;
a origem é o rascunho do Felipe.

**Proposta.** Sem migration: o **motivo é a natureza** (Vencido, Perda no preparo, Doação, Uso interno/degustação)
mais texto, obrigatório quando "Outro". Perda sai do lote escolhido (ou FIFO) e grava o custo do lote.
`GET api/producao/perdas?de&ate` agrupa por motivo e prato, em porções e em reais. Lote vencido com saldo vira
sugestão de "Lançar perda por vencimento" (não lança sozinho, D8).

**Console.** M2 › **Perdas**: lançar perda (prato ou insumo, lote, quantidade, motivo) e o resumo do período.

**Aceite.**
- [ ] Perda baixa do lote certo e entra no relatório com valor; estorno pelo Web desfaz.
- [ ] Ajuste de contagem (M2.1) **não** entra como perda (o card atual mistura os dois).
- [ ] Lote vencido aparece como sugestão, sem lançamento automático.

> **Como ficou (#1511):** as rotas são `POST`/`GET api/atendimento/producao/perdas` e `GET .../perdas/vencidos`. A
> perda sai por FEFO, ou do lote vencido sugerido, e não cria descoberto. O resumo não conta ajuste nem a baixa de
> insumo da produção (M2.4b), que também é Uso interno. Desfazer é o `POST api/estoque/estorno/{id}`.

**Fora.** Meta de perda; perda de pedido entregue (é ocorrência/estorno, M3).

### M2.7 · Embalagens

**Fato medido.** Necessidade não prevista no estudo nem na entrevista; fonte é o quadro "Cola de produção" do
Canva do Felipe (não medido aqui). No código, `ProdutoEmbalagem` é dimensão para frete, não estoque
(`ProdutoEmbalagem.cs:5-13`); `TipoEmbalagem` só diz se o produto é avulso ou embalado (`TipoEmbalagem.cs:14-18`).
O seed já tem fornecedor de "bandejas PET, selos térmicos e potes" (`CasaDaBabaSeed.cs:90`).

**Proposta.** Embalagem é **insumo** (`Produto` com `EhInsumo`, categoria de estoque "Embalagens", unidade Un,
mínimo). Entra na receita do prato como linha ("1 bandeja 800 g + 1 selo por porção") e desce na produção
(M2.2), porque é no porcionamento que a bandeja é usada. Sem baixa de receita ligada (D-M2-01 b), a contagem
manual do M2.1 cobre.

**Aceite.**
- [ ] Produzir 6 porções com receita que pede 1 bandeja por porção baixa 6 bandejas.
- [ ] Bandeja abaixo do mínimo aparece em "Comprar" (M2.3).

**Fora.** Embalagem por canal de entrega (sacola do Lalamove).

## 4. Ordem sugerida

```
M2.1 Estoque do dia ──► M2.2 Produção do dia ──► M2.6 Perdas
        │                       ▲
        └──► M2.3 Insumos ──► M2.4 Receitas ──► M2.5 Planejamento
                                   └──► M2.7 Embalagens
```

M2.1 primeiro: é o que a Thati usa todo dia e só liga backend pronto (tier baixo). M2.2, M2.4 e M2.6 dependem
das decisões abaixo.

## 5. Contradições e riscos medidos

1. **README §1 × este doc**: o README diz que "estoque e lotes" ficam no Web; aqui o console ganha lotes em porções e desacerto. Proposta: o Web fica com a **gestão** do inventário, o console com a **operação do dia** (tabela da §2).
2. **Estudo §3 × ADR-0056 item 7**: o estudo põe "compras, fornecedores" dentro do M2; o ADR deixa compras no Web. Aqui vale o ADR.
3. **Estudo §2 diz "Fichas técnicas ✅ Web"**: não existe tela de receita no Web; o "ficha técnica" do Web é a nutricional (`ProdutosController.cs`, `SalvarFichaTecnica`).
4. **`PUT` da receita sem policy** (`ProdutoComposicaoController.cs:7,37`): qualquer usuário logado troca a receita.
5. **Card "Perdas / Ajustes"** soma ajuste de contagem com perda (`MovimentacaoEstoqueRepository.cs:94-100`).
6. **FIFO**: RN-50 fala em "lote mais antigo"; o backend ordena por validade (`PedidoEstoqueIntegrationService.cs:80`).

## 6. Decisões pendentes do Felipe

> **Decididas em 09/10/2026 (Felipe, múltipla escolha):**
> - **D-M2-01 = a:** a baixa de insumo é só para produto com receita marcada "baixa automática"; falta de insumo avisa, não trava. Entra com a M2.4.
> - **D-M2-04 = a:** o destino da porção vem da linha do prato (servir × preparar em casa). Nada novo no lote.
> - **D-M2-06 = a:** o Operador lança a produção e ajusta o saldo; ver e mexer no estoque ainda exige a permissão de estoque. Perda acima de um valor pede Gerente (M2.6).
>
> - **D-M2-05 = a** (09/10, M2.5, #1502): a sugestão é `max(mínimo + agendados + descoberto − saldo, 0)` por prato. Agendados são os pedidos antes de "pronto", que é quando o pedido baixa o estoque. A lista de compras fica no console, pelo `POST api/listas-compras/gerar`, e não abre no Web, que saiu (ADR-0059).
>
> - **D-M2-02 = a** (09/10, M2.6, #1511): o motivo vem da lista e vira a natureza da saída (Vencido → Vencimento, Perda no preparo → Perda, Doação → Doação, Degustação → Uso interno, Outro → Prejuízo). Texto obrigatório só em "Outro".
> - **D-M2-06, o limite** (09/10, #1511): perda acima de R$ 50, pelo custo dos lotes, só o Gerente lança. Desfazer é o estorno de saída (Gerente), pelo botão no console, porque o Web saiu.
>
> Segue pendente: D-M2-03.

**D-M2-01 · A produção baixa insumo pela receita?**
- a) Sim, só para produto com receita marcada "baixa automática"; falta de insumo avisa, não trava **(Recomendado)**
- b) Não; receita serve só para planejar e custear, insumo é contado à mão
- c) Sim, sempre que houver receita

**D-M2-02 · Perda exige motivo?**
- a) Sim, motivo da lista (vencido, perda no preparo, doação, degustação) e texto só em "Outro" **(Recomendado)**
- b) Sim, texto livre obrigatório
- c) Não, motivo opcional como hoje

**D-M2-03 · Onde a embalagem desce do estoque?**
- a) Na produção, pela receita do prato **(Recomendado)**
- b) Na venda, por item do pedido
- c) Só por contagem manual periódica

**D-M2-04 · O destino da porção (comer agora × congelado) é dado do lote?**
- a) Não; vem da linha do item do cardápio (servir × preparar em casa, RN-17) **(Recomendado)**
- b) Sim, enum novo no item do lote, como o protótipo propôs
- c) Não registrar destino

**D-M2-05 · O que a sugestão de produção soma?**
- a) Mínimo − saldo + pedidos agendados sem lote + descoberto aberto **(Recomendado)**
- b) Só mínimo − saldo (US-066 literal)
- c) Sem sugestão; ela digita o que vai produzir

**D-M2-06 · Quem pode lançar produção e perda?**
- a) Perfis com o módulo M2 (dona e cozinha); perda acima de um valor pede Gerente **(Recomendado)**
- b) Qualquer perfil logado, como hoje
- c) Só a dona
