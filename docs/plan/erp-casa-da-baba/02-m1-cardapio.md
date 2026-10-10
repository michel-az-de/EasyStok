# M1 · Cardápio

Issue: #1316 · Plano: [README](README.md) · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md)
Base medida: master `b713263a` (worktree da #1316), 01/10/2026. Leitura somente.
Telas do rascunho do Felipe: **produtos, categorias, preços, combos, disponibilidade**.

Fontes do jeito de operar: protótipo `C:\rep\casa-da-baba-atendimento\prototipo-omni` (domínio copiado em
`EasyStock.Console/src/dominio/cardapio.js`), `EasyStock.Console/src/features/cardapio/PainelCardapio.jsx`,
entrevista `Downloads/02-ESTORIAS-DE-USUARIO.md` (E3) e `03-ANALISE-DIRETRIZES-CASOS-DE-USO.md` (RN-15 a RN-19, UC-12).

## 1. Veredito

**O CARDÁPIO EXISTE NA API E NÃO EXISTE NO CONSOLE.** O console lê o cardápio para montar a comanda e mais
nada: incluir, editar, ligar/desligar e ajustar saldo estão em `NAO_LIGADAS`
(`EasyStock.Console/src/aplicacao/api/naoLigadas.js:34-35`). No backend há três peças pela metade:
variações (gravadas, mas não chegam ao site nem ao pedido), seções (tabela sem CRUD) e dois preços.
Combos e adicionais não existem.

| Tela | Backend | Console | Web (legado) | Fatia |
|---|---|---|---|---|
| Lista e dia (ligar, ocultar, ordem) | ✅ | ⬜ só leitura | ✅ `Cardapio/Index` | M1.1 |
| Item (incluir, editar, tirar) | ✅ sem validação/novidade | ⬜ | ✅ `Cardapio/Form` | M1.2 |
| Categorias | 🟡 `CardapioSecao` sem CRUD | ⬜ | 🟡 só texto | M1.3 |
| Preços e porções | 🟡 variação não chega ao pedido | ⬜ preço único | ✅ editor de opções | M1.4 |
| Adicionais | ⬜ | 🟡 só no modo demonstração | ⬜ | M1.5 |
| Combos | ⬜ | ⬜ | ⬜ | M1.6 |
| Disponibilidade | ✅ flag manual + saldo informativo | 🟡 regra no front | ✅ toggle | M1.7 |
| Giro do item | 🟡 ranking por produto, não por item | ⬜ | 🟡 analytics | M1.8 |

## 2. Modelo canônico (PROPOSTA, decide D-M1-01)

**Fato medido.** Hoje o preço e a categoria do item vêm de uma cadeia de herança:

| Atributo | Regra atual | Onde |
|---|---|---|
| Preço | `PrecoStorefront` do item; senão `Produto.PrecoReferencia`; senão 0 | `CardapioItem.cs:232-241` |
| Preço da porção | `CardapioItemVariacao.PrecoStorefront` (absoluto, não delta) | `CardapioItemVariacao.cs:36-37` |
| Categoria | `Secao.Nome`, senão `CategoriaTexto`, senão `Produto.Categoria.Nome` | `CardapioItem.cs:226` |
| Categoria de estoque | `Produto.CategoriaId` obrigatório (Guid, não nulo) | `Produto.cs:9` |
| Nome | `NomePublico`, senão `Produto.Nome` | `CardapioItem.cs:220` |

Um item pode ser **avulso** (sem `ProdutoId`, sem estoque) ou **vinculado** (`CardapioItem.cs:8-18`). Item avulso
nunca baixa estoque: a baixa ignora linha de pedido sem `ProdutoId` (`PedidoEstoqueIntegrationService.cs:64`).

**Proposta (alternativa A).** Separar o que o cliente vê do que existe, como a RN-47 e a US-062 pedem
("cardápio é o que o cliente vê, estoque é o que existe", `02-ESTORIAS-DE-USUARIO.md`, US-062):

```
 CARDÁPIO (M1, dono do preço e da categoria)          BASTIDOR (M2 e Web, dono do que existe)
 CardapioSecao ──< CardapioItem ──< CardapioItemVariacao ───► ProdutoVariacao ──< ItemEstoque (lotes)
                      │  (porção, preço da porção)                 │
                      ├─< adicional (outro CardapioItem)  M1.5      Produto ──< ProdutoComposicao (M2)
                      ├─< componente de combo             M1.6        (custo, insumo, categoria de estoque)
                      └── ProdutoId ─────────────────────────────►
```

| Regra | A (Recomendado) | B | C (status quo) |
|---|---|---|---|
| Preço de venda | `CardapioItem` / variação, obrigatório | `Produto.PrecoReferencia` | herança atual |
| `Produto.PrecoReferencia` | vira "preço sugerido" do bastidor, nunca cobra | é o preço | override opcional |
| Categoria que o cliente vê | `CardapioSecao` | `Categoria` de estoque | 3 fontes em cascata |
| `Categoria` de estoque | classifica compra e estoque (Web) | também é vitrine | idem |
| Custo e margem | `Produto.CustoReferencia` ou custo da receita (M2.2) | idem | idem |
| Risco | migração de dados de `CategoriaTexto` | `Produto` com categoria obrigatória vira vitrine; item avulso perde preço | dois preços divergem em silêncio |

Por que A: o protótipo já opera assim (preço e porção no item do cardápio, `infra/catalogo.js:73-79`), a
`CardapioSecao` foi feita desacoplada da categoria de estoque de propósito (`CardapioSecao.cs:9-11`), e a dona
mexe no preço do dia sem tocar no cadastro de estoque. Consequência: a fatia que grava preço (M1.2, M1.4)
deixa de herdar `PrecoReferencia` para item vinculado novo; os existentes recebem o preço efetivo copiado
uma vez (script de dados, sem migration).

## 3. Fatias

### M1.1 · Gerir o cardápio no console (lista, dia, ordem)

**Fato medido.** A API de autoria existe em `api/minha-vitrine/cardapio` (`TenantVitrineCardapioController.cs:30`)
com listar, obter, incluir, editar, ligar/desligar visível e disponível, reordenar e remover (`:150-312`). Toda a
rota exige policy `Admin` (`:31`). O console usa só `GET api/atendimento/comanda/cardapio`
(`AtendimentoComandaController.cs:31`, `comandaApi.js:123-124`).

| Peça | Backend | Onde |
|---|---|---|
| Listar e obter | `GET minha-vitrine/cardapio`, `GET .../{itemId}` | `TenantVitrineCardapioController.cs:150,161` |
| Ligar/desligar do dia | `POST .../{id}/toggle-disponivel` | `:280` |
| Mostrar/ocultar no site | `POST .../{id}/toggle-visivel` | `:265` |
| Ordem (double, insere entre) | `POST .../{id}/reordenar` | `:295`, `CardapioItem.cs:79` |

**Lacunas.**

| # | Lacuna | Protótipo | Backend hoje | Correção |
|---|---|---|---|---|
| 1 | Operador comum recebe 403 | a atendente liga e desliga o dia | policy `Admin` na classe | permissão por módulo M1 (M0.3); ligar/desligar do dia fica com o perfil de atendimento |
| 2 | Duas listas para a mesma coisa | uma lista só | comanda lê o menu público, gestão lê o admin | console usa `minha-vitrine/cardapio` para gerir e a comanda continua no `comanda/cardapio` |

**Console.** Módulo M1 › **Itens**: tabela com foto, nome, linha, porção, preço, situação (M1.7), visível no site e
ordem por arrastar. No balcão (M3) o `PainelCardapio` mantém a aba **Gerir** do dia (ligar/desligar, ajustar saldo)
para não tirar a dona do cockpit (D5). Absorve a parte "cardápio" da F11 (#1241); a F11 fica com o lote de papel.

**Decisão já tomada.** Tela esconde, API nega (README §2.2). Ligar/desligar não apaga nada.

**Aceite.**
- [ ] Ligar/desligar, ocultar e reordenar pelo console persistem e sobrevivem a recarregar.
- [ ] Item desligado some da comanda como "Fora do cardápio de hoje", não some da lista de gestão (RN-16).
- [ ] Perfil sem M1 vê o card bloqueado; a API devolve 403 (teste de integração).

**Fora.** Edição do item (M1.2); categorias (M1.3).

### M1.2 · Item: incluir, editar, tirar e repor

**Fato medido.** `AdicionarItemRequest`/`EditarItemRequest` já cobrem nome, texto de categoria, descrição,
ingredientes, alérgenos, molho sugerido, foto, preço, tag, porção, filtros, opções, seção, linha, tempo de
preparo e instrução de finalização (`TenantVitrineCardapioController.cs:64-106`). Item nasce oculto
(`CardapioItem.cs:177`). Remover é **hard delete** (`RemoverCardapioItemAdminUseCase.cs:14`). Tag aceita só
`assinatura`, `novo`, `vegetariano`, sem prazo (`CardapioItem.cs:39-40`). Alérgenos são texto livre no item
(`CardapioItem.cs:83`) e lista no `Produto.AtributosJson` (`ProdutoFichaTecnica.cs:22-24`).

**Lacunas.**

| # | Lacuna | Protótipo / entrevista | Backend hoje | Correção |
|---|---|---|---|---|
| 1 | Item novo "em validação" (RN-15) | nasce em validação, agente não oferece (`dominio/cardapio.js:148,172,196`) | não existe | `EmValidacao` + `ConfirmarValidacao()`; ferramenta do agente filtra |
| 2 | Novidade com prazo (US-029) | `novidadeAte` (`dominio/cardapio.js:151`) | tag `novo` sem prazo | `NovidadeAte` (data); tag `novo` passa a ser derivada |
| 3 | Tirar sem apagar | `removidoEm`, alterna (`dominio/cardapio.js:203`) | hard delete | `ArquivadoEm`; DELETE só para item nunca vendido |
| 4 | Alérgenos com duas fontes | ficha do item | texto no item × lista no produto | decide D-M1-06; #1314 (PR #1315) já leva o texto do item ao agente |

**Console.** M1 › Itens › **Incluir/Editar**: o `FormularioItemCardapio` (`PainelCardapio.jsx:395`) ganha os
campos que a API já tem (foto, descrição, ingredientes, alérgenos, instrução de finalização, tempo de preparo).
Rótulos "Em validação" e "Novidade até dd/mm" na lista.

**Decisão já tomada.** SKU nunca muda depois de criado (`dominio/cardapio.js`, comentário de `editarItemCardapio`);
pedido guarda snapshot de nome e preço (`CheckoutCoreService.cs:298-303`). Tier **alto** (migration aditiva).

**Aceite.**
- [ ] Item incluído pelo console nasce oculto e em validação; agente não oferece até "Confirmar" (teste da ferramenta).
- [ ] Novidade some sozinha depois do prazo (teste de domínio com relógio fixo).
- [ ] "Tirar do cardápio" arquiva; pedido antigo continua mostrando o item; "Repor" volta.
- [ ] Bloco `DO $rls$` não muda (colunas novas em tabela existente); gate verde.

**Fora.** Painel de giro (M1.8); cardápio em imagem (US-023, S48).

### M1.3 · Categorias do cardápio (seções)

**Fato medido.** `CardapioSecao` tem até 3 níveis, ordem e visível (`CardapioSecao.cs:22-40`) e já tem
configuração EF, mas **nenhum use case, endpoint ou tela** (busca por `CardapioSecao` só acha entidade,
configuração e os use cases de item que gravam `SecaoId`). O seletor de seção do Web (#653, fechada) não está em
`EasyStock.Web/Views/Cardapio/Form.cshtml` (zero ocorrências). O menu público já usa `Secao.Nome` primeiro
(`CardapioItem.cs:226`). O protótipo não tem categoria, só **linha** (servir/casa, `infra/catalogo.js:67-70`).

| Peça | Backend | Onde |
|---|---|---|
| Entidade e regras | `CriarRaiz`, `CriarSubsecao`, profundidade 3 | `CardapioSecao.cs:52-68` |
| Vínculo do item | `CardapioItem.SecaoId`, `DefinirSecao` | `CardapioItem.cs:70,468` |
| Texto livre legado | `CardapioItem.CategoriaTexto` | `CardapioItem.cs:63` |

**Lacunas.** CRUD (`api/minha-vitrine/secoes`: listar, criar, renomear, reordenar, ocultar, excluir vazia);
migração de `CategoriaTexto` para seções de nível 0 (script idempotente, uma seção por texto distinto);
`CategoriaTexto` vira somente leitura.

**Console.** M1 › **Categorias**: lista com arrastar para ordenar e contador de itens. A comanda agrupa por
seção dentro da linha.

**Decisão já tomada.** Linha (servir × preparar em casa, RN-17, `EasyStock.Domain/Enums/Storefront/LinhaProduto.cs:7-11`) é atributo do item, não categoria.

**Aceite.**
- [ ] CRUD com teste de isolamento por empresa e de seção não vazia recusada.
- [ ] Script converte os `CategoriaTexto` atuais e o `GET api/storefront/{slug}/menu` devolve as mesmas categorias de antes.
- [ ] Console cria, renomeia e ordena; a ordem aparece na comanda e no site.

**Fora.** Reparent de seção (fora da v1 do ADR-0035).

### M1.4 · Porções e preços ponta a ponta (variações)

**Fato medido.** Variação tem rótulo, preço absoluto, SKU, disponível e liga a `ProdutoVariacao`
(`CardapioItemVariacao.cs:28-52`). O admin grava (`Opcoes`, `TenantVitrineCardapioController.cs:80,100`), mas:
o DTO público não tem opções (`CardapioItemPublicoDto.cs:23-37`, #651 aberta); o checkout recebe só
`CardapioItemId` (`CheckoutCoreService.cs:18`) e cobra `PrecoEfetivo()` do item guarda-chuva (`:298`); os campos
`CardapioItemVariacaoId`, `VariacaoRotuloSnapshot` e `SkuSnapshot` de `PedidoItem` (`Pedido.cs:346-352`) ficam
nulos; o console ignora `opcoes` (`comandaApi.js:14-23`). Estoque já tem `ItemEstoque.ProdutoVariacaoId`
(`ItemEstoque.cs:11`), mas produção grava `ProdutoVariacaoId: null` (`RegistrarProducaoUseCase.cs:130`) e a baixa
busca só por produto (`PedidoEstoqueIntegrationService.cs:78-80`).

**Lacunas.**

| # | Lacuna | Correção |
|---|---|---|
| 1 | Site e console não veem porções | emitir `opcoes` no menu público e no `comanda/cardapio` (#651, contrato aditivo, ETag) |
| 2 | Pedido cobra o preço errado | `ItemPedidoCheckout.VariacaoId`; preço = variação; snapshot de rótulo e SKU |
| 3 | Porção sem saldo próprio | decide D-M1-03; recomendado: produção e baixa por `ProdutoVariacao` |
| 4 | `PrecoReferencia` ainda cobra item vinculado sem preço | modelo A: preço obrigatório no item ou na variação |

**Console.** Comanda: item com porções abre a escolha da porção (como o site). M1 › Itens: editor de porções
(rótulo, peso, preço, padrão, disponível).

> **Como fica a entrega (09/10):** três fatias, com o console primeiro.
> - **M1.4a (#1529):** editor de porções no console, cada porção ligada à sua `ProdutoVariacao`.
> - **M1.4b:** vender por porção (pedido e comanda).
> - **M1.4c:** saldo por porção (produção e baixa).
>
> O menu público com `opcoes` (lacuna 1, Fase 2) fica para depois da confirmação do site.
>
> **Entregue (10/10):** M1.4a (#1529), M1.4b (#1531) e M1.4c (#1537). No caminho apareceu um bug: o pedido do site e da
> comanda nasce sem loja e não baixava estoque. Foi corrigido na #1534: sem loja, a baixa sai do estoque da empresa.
> Na M1.4c, a linha de porção sem nenhum lote daquela porção baixa do lote sem porção do prato (estoque antigo ou PWA).

**Decisão já tomada.** Preço da variação é absoluto, não delta (ADR-0035). Tier **alto** (pedido e contrato
público; coordenar com o repositório do site antes da fase 2 do ADR-0035).

**Aceite.**
- [ ] Pedido do site e da comanda com "Ravióli 800 g" cobra o preço da porção e grava rótulo e SKU.
- [ ] Teste de contrato do menu público v2 e ETag estável.
- [ ] Porção esgotada não entra no checkout; a outra porção do mesmo item entra.

**Fora.** Preço por canal; preço promocional (M6).

### M1.5 · Adicionais (extras vendidos à parte)

**Fato medido.** No protótipo, adicional é **SKU do próprio cardápio**, com preço, porção e saldo, e um mapa
diz quais adicionais cada prato oferece (`infra/catalogo.js:213-235`, `dominio/cardapio.js:118`). Motivo: "ela vende
parmesão e molho à parte e combina por texto, o que cobra errado" (comentário em `catalogo.js:217-219`, áudio 02,
US-022, RN-19). O backend não tem nada: `PedidoItem` só tem `Observacao` (`Pedido.cs:337`).
Atenção ao nome: "acréscimo" no console é **item que entrou na comanda depois do pagamento**
(`dominio/pedido.js:33-35`), outra coisa.

**Proposta.** Tabela `CardapioItemAdicional` (item, item adicional, ordem; `EmpresaId` + RLS). No pedido o
adicional entra como linha própria com `AdicionalDeItemId` apontando para a linha do prato, para a cozinha e o
canhoto mostrarem "+ Parmesão (80 g)" embaixo do prato. Baixa de estoque igual a qualquer item.

**Console.** Comanda: botões dos adicionais sob o prato escolhido. M1 › Itens: seleção dos adicionais do prato
(o `FormularioItemCardapio` já tem, `PainelCardapio.jsx:402,410`).

**Aceite.**
- [ ] Adicional aparece no menu público do prato, entra no checkout como linha ligada e soma no total.
- [ ] KDS e canhoto mostram o adicional sob o prato.
- [ ] Adicional sem saldo avisa e não trava (D7).

**Fora.** Adicional com preço diferente por prato.

### M1.6 · Combos

**Fato medido.** Não existe em lugar nenhum. O único rastro é texto de observação de venda no seed
("Venda balcão - combo massa + molho", `EasyStock.Api/Data/Tenants/CasaDaBabaSeed.cs:225`). A entrevista não cita
combo; a origem é o rascunho do Felipe. Pedido não tem desconto (`Pedido.cs:77`, F15 propõe `Pedido.Desconto`).

**Proposta (depende de D-M1-02).** `CardapioItem.Tipo` (`Simples` | `Combo`) e `CardapioComboComponente`
(combo, item componente, variação opcional, quantidade, grupo de escolha opcional). O combo tem preço próprio.
No pedido: uma linha do combo com o preço e linhas filhas de preço zero, uma por componente, ligadas pelo mesmo
`AdicionalDeItemId` do M1.5 (renomeado para `ItemPaiId`). Cozinha e estoque leem as filhas; o caixa lê o combo.

**Aceite.**
- [ ] Combo de preço fixo cobra o preço dele; componentes baixam estoque, cada um no seu lote (FIFO).
- [ ] Componente fora do dia torna o combo "fora do dia" (situação derivada, M1.7).
- [ ] Giro (M1.8) conta o combo e, à parte, os componentes vendidos dentro dele.

**Fora.** Combo com desconto percentual sobre a soma (é cupom, F15); combo por canal.

### M1.7 · Disponibilidade com a regra na API

**Fato medido.** Disponível é flag manual; saldo é informativo e **não esgota** o item (#1171,
`ListarCardapioPublicoUseCase.cs:89-91`); o checkout recusa item invisível ou indisponível
(`CheckoutCoreService.cs:275-277`); expediente governa o atendimento e só a loja forçada fechada recusa o site
(`ExpedienteLoja.cs:22-27`); janela é capacidade (`CheckoutCoreService.cs:142-175`). No console a regra das quatro
situações vive no front (`dominio/cardapio.js:51-84`: fora do dia, esgotado vendável com alerta, pouco, disponível).

| Situação | Quem decide | Vende? | Fonte |
|---|---|---|---|
| Fora do cardápio de hoje | a dona, à mão | não | `CardapioItem.Disponivel` |
| Esgotado (saldo 0) | lotes | sim, com alerta de desacerto | RN-48, S17, S22 |
| Pouco (≤ 2 porções) | lotes | sim | protótipo |
| Loja fechada | expediente manual | site não; balcão sim | S40 |

**Lacunas.** A API devolve `situacao` e `rotuloSituacao` por item (regra na API, ADR-0054 item 2); alternativas
para quem ouviu "não" (mesma linha primeiro, `dominio/cardapio.js:103`) num endpoint que o agente
e a comanda usam; registrar interesse já existe (S31).

**Aceite.**
- [ ] Teste de use case das quatro situações; o console deixa de calcular e só mostra.
- [ ] Alternativas excluem item fora do dia, arquivado e sem saldo.

**Fora.** Disponibilidade por dia da semana (ver D-M1-05).

### M1.8 · Giro do item (US-030, UC-12)

**Fato medido.** O ranking existente agrega por `Produto` (`IAnalyticsRepository.cs:418,432`). Item avulso
não tem produto, então fica de fora (inferência: confirmar na query de `GetTopProdutosAsync`). `PedidoItem`
guarda `CardapioItemId` (`Pedido.cs:344`), que basta para o giro do cardápio.

**Proposta.** `GET api/cardapio/giro?de&ate`: por item, quantidade, receita, data da última venda, e "nunca
vendeu" para os em validação (liga com RN-15).

**Aceite.**
- [ ] Teste com pedidos cancelados fora e combo contado à parte.
- [ ] M1 › **Giro**: tabela ordenável e filtro de período; botão "Tirar do cardápio" na linha (M1.2).

**Fora.** Curva ABC e margem por item (depende do custo da receita, M2.2).

## 4. Fica no EasyStock.Web

Cadastro completo de `Produto` (fotos, variações de estoque, ficha nutricional, histórico:
`EasyStock.Web/Controllers/ProdutosController.cs`), categorias de estoque (`CategoriasController.cs`). A tela
`Cardapio` do Web (`CardapioController.cs`) é **substituída** pelo M1 quando M1.1 a M1.4 entrarem; até lá as duas
gravam na mesma API.

## 5. Contradições e riscos medidos

1. **Duas fontes de alérgeno**: texto no item (`CardapioItem.cs:83`) × lista no produto (`ProdutoFichaTecnica.cs:22-24`). Risco de saúde (#1314).
2. **#653 fechada, seletor de seção ausente** no `Cardapio/Form.cshtml`; **#651 aberta**: variações gravadas e nunca cobradas.
3. **Remover apaga** (`RemoverCardapioItemAdminUseCase.cs:14`) × protótipo "tirar nunca apaga".
4. Comentários de domínio dizem "Casa da Baba sem ERP" (`CardapioItem.cs:13`, `CardapioSecao.cs:9-10`), mas desde S23 ela usa lotes e estoque.
5. O "ficha técnica" do `api/produtos/{id}/ficha-tecnica` (`ProdutoController.cs:339`) é **nutricional**, não receita: nome colide com M2.2.

## 6. Decisões pendentes do Felipe

> **Decididas em 08/10/2026 (Felipe, múltipla escolha):**
> - **D-M1-01 = a:** o cardápio é dono do preço (item/porção) e da categoria (seção).
> - **D-M1-05 = a:** o item fora do cardápio de hoje só volta quando ela religa.
> - **D-M1-07 = a:** tirar arquiva e é diferente de ocultar do site; apagar só item nunca vendido.
> - **D-M1-08 = a:** o item novo nasce em validação, e o agente não oferece até ela confirmar.
>
> Permissão no console (#1241): o Operador mexe no dia e no saldo; o Gerente inclui, edita e tira.
> **Decididas em 09/10/2026 (Felipe, múltipla escolha):**
> - **D-M1-02 = a:** o combo tem preço e componentes fixos, e cada componente baixa o próprio estoque.
> - **D-M1-03 = a:** a porção tem saldo próprio pela `ProdutoVariacao`; a produção e a baixa passam a usar a porção.
> - **D-M1-04 = a:** o adicional é item do próprio cardápio e entra como linha própria ligada ao prato, com saldo e preço.
> - **D-M1-06 = a:** os alérgenos são uma lista fechada no item, mais um texto "outros".
> - **ADR-0035, Fase 2:** as porções entram primeiro no console. O menu público só emite `opcoes` depois de o Felipe confirmar que o site lê o schema v2.
>
> Todas as decisões do M1 estão tomadas.

**D-M1-01 · Quem é dono do preço e da categoria que o cliente vê?**
- a) O cardápio: preço no item ou na porção, categoria na seção; `PrecoReferencia` vira sugestão do bastidor **(Recomendado)**
- b) O produto de estoque: `PrecoReferencia` e `Categoria` valem para o site
- c) Manter a herança atual (item sobrescreve, senão produto)

**D-M1-02 · O que é um combo?**
- a) Preço fixo, componentes fixos, cada componente baixa o próprio estoque **(Recomendado)**
- b) Preço fixo com escolhas por grupo ("1 massa + 1 molho à escolha"), baixa do que foi escolhido
- c) Desconto sobre a soma dos itens (vira cupom automático da F15)
- d) Adiar combos até a Thati pedir (não aparece na entrevista)

**D-M1-03 · Porção diferente do mesmo prato (300 g × 800 g) tem saldo próprio?**
- a) Sim, por `ProdutoVariacao` (o estoque já tem a coluna); produção e baixa passam a usar a variação **(Recomendado)**
- b) Sim, cada porção vira um `Produto` separado
- c) Não, as porções dividem o saldo do produto

**D-M1-04 · Como entra o adicional (parmesão, molho à parte)?**
- a) Item do próprio cardápio, linha própria ligada ao prato, com saldo e preço (como o protótipo) **(Recomendado)**
- b) Opção dentro do item com preço somado, sem estoque
- c) Continua como observação de texto

**D-M1-05 · "Fora do cardápio de hoje" volta sozinho?**
- a) Não, só quando ela religa (como protótipo e API hoje) **(Recomendado)**
- b) Volta sozinho na abertura do dia seguinte
- c) Agenda por dia da semana

**D-M1-06 · Fonte única dos alérgenos?**
- a) Lista fechada no item do cardápio (glúten, lactose, ovo, castanhas, ...) mais texto "outros" **(Recomendado)**
- b) Texto livre no item, como hoje
- c) A ficha nutricional do produto de estoque

**D-M1-07 · "Tirar do cardápio" apaga?**
- a) Não, arquiva e pode repor; apagar só item nunca vendido **(Recomendado)**
- b) Apaga, como a API faz hoje

**D-M1-08 · Item novo nasce "em validação" (RN-15)?**
- a) Sim, o agente não oferece até ela confirmar **(Recomendado)**
- b) Não, basta o "visível no site"
