# ADR-0035: Cardápio v2 — variações com preço por opção, seções hierárquicas e contrato aditivo

**Status:** Accepted
**Data:** 2026-06-18
**Deciders:** Felipe Azevedo
**Épico:** #645
**Estende (não supersede):** ADR-0031 (modos avulso/vinculado seguem vivos)

---

## Contexto

Um prato como **"Ravioli de Abóbora — 300g R$28 / 800g R$42"** só existia como texto livre em
`CardapioItem.PesoExibicao` ("300g / 800g") com um preço único, ou como **dois `CardapioItem`** — a
postura explícita do ADR-0031 ("dois tamanhos = dois Produtos/itens"). Isso polui a vitrine, duplica
foto/descrição e perde a noção de "mesmo prato".

Faltava também uma **estrutura de categorias** rica o bastante para um menu de restaurante (seções) e
uma loja de departamentos (departamento → categoria → subcategoria): hoje o cardápio público achata
tudo numa única string de categoria.

Esta evolução entrega um **item guarda-chuva único** com seleção inteligente (tamanho → preço) e
**seções hierárquicas**, preservando rastreabilidade (SKU até o pedido), testabilidade e o contrato
consumido pela vitrine externa Casa da Baba.

## Decisão

### 1. Variação storefront-centric (`CardapioItemVariacao`)
Entidade POCO filha de `CardapioItem`, com **preço absoluto por opção** (`decimal(10,2)`), `Sku` opcional
e `ProdutoVariacaoId` opcional (FK RESTRICT) para rastreabilidade ERP — só quando o item pai é vinculado
e a variação é do mesmo Produto. Funciona para itens **avulsos** (sem ERP) e **vinculados**.

*Rejeitado:* pôr preço em `ProdutoVariacao` (forçaria ERP em quem não tem — contra o ADR-0031); grupos
de modificadores completos (`GrupoOpcao`) agora (peso sem payoff — é a costura de evolução, ver §7).

- `EhPadrao` é invariante **de agregado** (≤1 por item, orquestrado por `CardapioItem.DefinirVariacaoPadrao`),
  **sem índice de banco** — índice parcial não é `DEFERRABLE` no Postgres, o que quebraria a troca de padrão.
- `Rotulo` **preserva a caixa** digitada; unicidade por item é case-insensitive via **coluna gerada
  `rotulo_lower` + UNIQUE CONSTRAINT DEFERRABLE INITIALLY DEFERRED** (índice de expressão também não é
  deferível → swap "P"↔"G" falharia em UPDATE in-place). Criada via raw SQL na migration.
- Preço/peso/disponibilidade exibidos = **opção mais barata disponível** (`EhPadrao` se disponível;
  senão a mais barata disponível; senão a mais barata absoluta). Nunca exibe opção esgotada.

### 2. Seção hierárquica (`CardapioSecao`) desacoplada do ERP
Entidade auto-referenciada, dona pelo `Storefront`, `Nivel 0..2` (CHECK; profundidade máx. 3 níveis).
`CardapioItem.SecaoId` é FK **SET NULL** (apagar seção solta os itens, não os apaga).
`CategoriaEfetiva()` passa a ser `Secao?.Nome → CategoriaTexto → Produto?.Categoria?.Nome`.

*Rejeitado:* reusar `Categoria` do ERP (re-acoplaria o cardápio ao ERP justamente para tenants sem ERP).

**Reparent fora da v1** (apenas criar/renomear/reordenar/excluir) → `Nivel` é estável na criação, sem
drift. Excluir seção com filhas é bloqueado (`23503`) com mensagem amigável. Reparent futuro exigirá
recompute de `Nivel` da subárvore + revalidação de profundidade.

### 3. Rastreabilidade no pedido por snapshot (sem FK)
`PedidoItem` ganha colunas nullable `CardapioItemId`, `CardapioItemVariacaoId`, `ProdutoVariacaoId`,
`VariacaoRotuloSnapshot`, `SkuSnapshot` — **as refs storefront sem FK** (puro trace/snapshot, como
`PedidoItem.Nome` já é). Deletar/editar uma opção nunca quebra um pedido histórico. Na consolidação
`Pedido → Venda`, `ProdutoVariacaoId` é copiado para `ItemVenda` (que tem a FK real). Groundwork de #263.

### 4. Edição = reconciliação keyed-by-Id (não replace-set cego)
No editar do admin: opção com `Id` no payload e no banco → update preservando `Id`; só no payload →
insert; só no banco → delete (seguro, pois pedidos guardam snapshot). Idempotência ao nível
`(item, lower(rotulo))` garantida pela constraint deferível.

### 5. Contrato público aditivo, 3 fases
O item ganha `opcoes[]` (opcional) e `categoriaPath[]` (root→folha) + `schemaVersion: 2` no envelope.
Chaves v1 mantidas; para guarda-chuva: `precoCentavos` = opção mais barata disponível ("a partir de"),
`pesoExibicao` = peso dessa opção, `disponivel` = qualquer opção disponível, `estoqueAtual` = null.
Consumidor usa **item-level `precoCentavos`** para "a partir de", **nunca `opcoes[0]`** (ordem de
exibição ≠ ordem de preço).

**Rollout:** Fase 1 — schema v2 relaxado (`additionalProperties` afrouxado) vai para a vitrine **primeiro**;
Fase 2 — API passa a emitir (gated em confirmação humana cross-repo); Fase 3 (futura) — re-fechar +
`required`. **Mitigador real do rollout = back-compat pela opção padrão**; `schemaVersion` é só sinal de
branch (mesma cooperação cross-repo do gate manual), não mitigação.

### 6. Concorrência: last-write-wins (v1)
`CardapioItem` **não** ganha token de concorrência na v1 (negócio de 2 pessoas; a reconciliação
keyed-by-Id já mescla edições a opções distintas). Adicionar `xmin` afetaria todos os write-paths de
`CardapioItem` (toggle/reorder/edit/remove) — fica como upgrade futuro auditado.

### 7. Costura de evolução
`GrupoOpcao` (escolha única/múltipla, obrigatório, min/max, adicionais +R$) quando houver demanda —
`CardapioItemVariacao` vira filha de `GrupoOpcao`. Reparent de seção e `xmin` idem.

## Consequências

**Fica mais fácil:** card único com seleção tamanho→preço; menu organizado por seções; rastreabilidade
até o SKU no pedido futuro; vitrine reutilizável.

**Fica mais difícil / atenção:**
- +1 handshake **manual** de schema com a vitrine (cross-repo) antes da Fase 2.
- Dado legado **não** migra automaticamente — os pares existentes (ex.: "Lasanha P"/"Lasanha G") são
  re-autorados manualmente pela tenant. Fora de escopo nesta v2.
- Determinismo do ETag depende do **sort in-memory** (tupla estruturada de seção→item); split query só
  resolve cardinalidade e não é transacional (torn-read aceito p/ menu cacheado 5 min).

## Migration & invariantes
- Migration em dois blocos (transacional + `suppressTransaction` para `CREATE INDEX CONCURRENTLY` nas
  tabelas populadas `cardapio_item`/`pedido_itens`); gerada via `dotnet ef` com `.Designer.cs` (ADR-0024).
- `Down()` derruba explicitamente a constraint deferível, a coluna `rotulo_lower` e os índices raw.
- Tabelas novas **sem `EmpresaId`** (escopo via `Storefront`, consistente com `cardapio_item`).
- CHECK `nivel BETWEEN 0 AND 2`; `PrecoStorefront >= 0`; `Rotulo` não vazio.

## Fatiamento
Sub-issues #646 (domínio+configs) · #647 (migration) · #648 (leitura) · #649 (projeção) · #650 (contrato
v2) · #651 (API emite) · #652 (admin Api) · #653 (admin Web).
