# ADR-0031: Módulo Cardápio Produto-Agnóstico com Gestão Self-Service

**Status:** Accepted  
**Data:** 2026-06-09  
**Deciders:** Felipe Azevedo  
**Issue:** #555  
**Número seguinte reservado:** ADR-0032 = plano de fuso UTC×BRT

---

## Contexto

O site Casa da Baba (Thatiane) consome `GET /api/storefront/{slug}/menu` — endpoint já funcional com cache ETag de 5 min. O gap é na gestão: `CardapioItem.ProdutoId` é `NOT NULL` (obriga criação de Produto no ERP antes de qualquer item no cardápio) e as Admin pages são SuperAdmin-only (`AdminPageBase` hardcoda `nivel == "SuperAdmin"`). Thatiane não consegue atualizar o próprio cardápio sem depender do Felipe.

---

## Decisão

### 1. CardapioItem produto-agnóstico

`ProdutoId: Guid` → `Guid?`. Dois modos coexistem:

| Modo | ProdutoId | Nome exibido | Preço (domínio = R$) | Categoria |
|------|-----------|-------------|---------------------|-----------|
| **Avulso** | `null` | `NomePublico` (required no domínio) | `PrecoStorefront` (required no domínio) | `CategoriaTexto` |
| **Vinculado** | FK Produto | `NomePublico ?? Produto.Nome` | `PrecoStorefront ?? Produto.PrecoReferencia.Valor` | `CategoriaTexto ?? Produto.Categoria.Nome` |

**Campos novos em `CardapioItem`:**
- `NomePublico: string?` (varchar 200) — armazenado em lowercase; CSS `text-transform` na exibição
- `CategoriaTexto: string?` (varchar 100) — armazenado em lowercase

**Unicidade de vinculado:** O índice `uq_cardapio_item_storefront_produto (StorefrontId, ProdutoId)` é mantido. Um Produto = uma entrada por storefront. "Lasanha P" e "Lasanha G" com tamanhos distintos = dois Produto distintos no ERP (SKUs diferentes). Para tenants sem ERP: itens avulsos com nomes distintos.

**Preço:** sempre `decimal` (R$) no domínio. Centavos (`long`) só no DTO de saída via `(long)Math.Round(preco * 100m, MidpointRounding.AwayFromZero)`. `decimal(10,2)` no EF garante máximo 2 casas decimais — sem truncamento na conversão.

**EstoqueAtual no DTO:** vinculado = snapshot ERP; avulso = `null` (tipo `long?`). O frontend da Casa da Baba já usa `disponivel === false` (não `estoqueAtual`) para renderizar "Esgotado" — comportamento preservado.

---

### 2. Migration (dois blocos)

**Bloco transacional:**
```sql
ALTER TABLE cardapio_item ADD COLUMN IF NOT EXISTS nome_publico VARCHAR(200) NULL;
ALTER TABLE cardapio_item ADD COLUMN IF NOT EXISTS categoria_texto VARCHAR(100) NULL;
-- PG 12+: DROP NOT NULL é operação de metadado (sem table rewrite)
ALTER TABLE cardapio_item ALTER COLUMN produto_id DROP NOT NULL;
ALTER TABLE cardapio_item ADD CONSTRAINT IF NOT EXISTS chk_cardapio_item_nome_ou_produto
  CHECK (produto_id IS NOT NULL OR nome_publico IS NOT NULL);
```

**Bloco com `suppressTransaction: true` (padrão estabelecido — ex: 20260513000002_AddVendaReportingColumns):**
```sql
-- Substitui índice anterior (sem condição)
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS uq_cardapio_item_storefront_produto
  ON cardapio_item(storefront_id, produto_id)
  WHERE produto_id IS NOT NULL;

-- Novo índice para avulso (evita duplicatas por engano)
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS uq_cardapio_item_storefront_nome_avulso
  ON cardapio_item(storefront_id, LOWER(nome_publico))
  WHERE produto_id IS NULL AND nome_publico IS NOT NULL;
```

**Pré-migration (medir em prod):**
```sql
SELECT COUNT(*) FROM cardapio_item;  -- volume → decide se CONCURRENTLY é necessário
SELECT version();                    -- confirmar PG 12+ para DROP NOT NULL sem rewrite
```

**Verificação pós-migration (não COUNT de linhas — é invariante por construção em DDL puro):**
```sql
SELECT conname FROM pg_constraint WHERE conname = 'chk_cardapio_item_nome_ou_produto';
SELECT indexname FROM pg_indexes WHERE indexname LIKE '%cardapio_item%';
SELECT is_nullable FROM information_schema.columns
  WHERE table_name = 'cardapio_item' AND column_name = 'produto_id'; -- deve ser 'YES'
```

---

### 3. Fixes em infra existente

**`GetVisiveisDoStorefrontAsync` (repositório):** OrderBy SQL-side (`c.Produto!.Categoria!.Nome`) permanece — o `!` é anotação compile-time; EF traduz para LEFT JOIN + ORDER BY com NULLs por último no PG. Sem NRE.

**`ListarCardapioPublicoUseCase` — sort-key LINQ-to-Objects:**
```csharp
// Antes (não cobre avulso):
.OrderBy(i => i.Produto?.Categoria?.Nome ?? SemCategoriaSentinela)
// Depois:
.OrderBy(i => (i.CategoriaTexto ?? i.Produto?.Categoria?.Nome) ?? SemCategoriaSentinela)
```

**`GetProdutoIdsDoStorefrontAsync`:** filtrar nulls para manter retorno `IReadOnlyCollection<Guid>` (caller `AdicionarCardapioItemAdminUseCase` usa como filtro de dropdown — avulsos corretos ficando fora):
```csharp
.Where(c => c.StorefrontId == storefrontId && c.ProdutoId.HasValue)
.Select(c => c.ProdutoId!.Value)
.ToListAsync(ct)
```

**Escopo por EmpresaId vs SuperAdmin bypass:**
```csharp
// empresaId: null = SuperAdmin (acessa qualquer storefront)
// empresaId: hasValue = Admin tenant (escopo restrito ao seu EmpresaId)
Task<CardapioItem?> GetByIdAndScopeAsync(
    Guid storefrontId, Guid itemId, Guid? empresaId, CancellationToken ct);

// Resposta quando não encontrado: 404 (não 403) — não vaza existência
```

---

### 4. Auth de tenant (spike antes de qualquer mudança no Admin)

**Lacuna medida:** `AdminPageBase.IsSuperAdmin()` hardcoda `nivel == "SuperAdmin"` — `NivelAcesso.Admin` não é reconhecido pelo pipeline de auth do Admin.

**Spike (fatia 0 — zero commits) deve confirmar:**
1. Qual claim o JWT inclui para `nivel`/`nivelAcesso`?
2. Existe login flow para usuário com NivelAcesso ≠ SuperAdmin?
3. AdminPageBase pode ser estendido para aceitar `nivel == "Admin"` com verificação de EmpresaId?
4. O JWT inclui `empresaId` para usuários Admin?

**Se spike confirma:** modificar AdminPageBase + JWT generation conforme necessário.  
**Se spike revela gap:** abrir issue blocker antes das fatias 6+.

---

### 5. HorarioBrasil (pré-requisito do print endpoint)

Canônico: `EasyStock.Application/Common/HorarioBrasil.cs` — usa fallback IANA/Windows, cross-platform. **Não criar clone.** Adicionar:
```csharp
public static DateTime Agora() =>
    TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);
```
**Dono:** ADR-0032 (workstream fuso UTC×BRT). Este ADR é consumidor.

---

### 6. UX tenant

Formulário simplificado para Thatiane (sem campo "Motivo", campos avançados colapsados):

```
Nome do item*           (es-input text, max 200, required)
Preço (R$)*             (es-input number, step=0.01, min=0.01)
Categoria               (es-input text + datalist das existentes)
URL da foto             (es-input url, opcional)
Descrição               (es-input text, max 240, counter visual)
Publicar imediatamente  (es-checkbox, default: OFF)
                        — help: "Desmarcado = rascunho. Você publica quando estiver pronto."
[▸ Detalhes nutricionais] (colapsável)
```

Violações de constraint → mensagem amigável via catch de `PostgresException`:
- `SqlState "23505"` → "Já existe um item com esse nome no cardápio."
- `SqlState "23514"` → "Nome é obrigatório para itens sem produto vinculado."

Listagem tenant:
- Badge "Rascunho" para `Visivel=false`
- Botão "Publicar" inline (um clique, sem modal)
- Seletor de storefront quando empresa tem 2+ (auto-seleciona se houver exatamente 1)

---

### 7. Print endpoint

```
GET /api/storefront/{slug}/menu/imprimir
[AllowAnonymous]
Cache-Control: no-store
```
- Resolve slug via `storefrontRepository.GetBySlugAsync(slug)` (mesmo pattern do MenuController)
- Estampa data/hora com `HorarioBrasil.Agora()` (não `DateTime.UtcNow` — evita bug BRT+3h)
- Retorna HTML com `@media print` CSS (não JSON)

---

## Fatiamento de commits

| Fatia | O que muda |
|-------|-----------|
| **0 — Spike** | Leitura: JWT claims + login não-SuperAdmin + AdminPageBase (ZERO commits) |
| 1 | Domain: `CardapioItem` ProdutoId nullable + NomePublico + CategoriaTexto + `CriarAvulso()` |
| 2 | Migration: dois blocos (transacional + suppressTransaction CONCURRENTLY) |
| 3 | Infra: GetProdutoIds null-filter + GetByIdAndScope + sort-key use case |
| 4 | Application: ListarCardapioPublicoUseCase + AdicionarItemAvulsoUseCase + catch PostgresException |
| 5 | `HorarioBrasil.Agora()` no canônico (se ADR-0032 não entregou antes) |
| 6 | Admin auth: AdminPageBase aceita NivelAcesso.Admin com EmpresaId scope |
| 7 | Admin UI: Create.cshtml ProdutoId opcional + NomePublico + copy corrigido |
| 8 | Admin UI: Index.cshtml badge "Rascunho" + botão "Publicar" + seletor 1:N |
| 9 | Print endpoint `GET /api/storefront/{slug}/menu/imprimir` |
| 10 | Fixture Casa da Baba: item avulso + smoke test disponivel:true → "Adicionar" |

---

## Coerência de testes

**Domain:**
```csharp
CriarAvulso_NomeNulo_Throws()
CriarAvulso_PrecoNegativo_Throws()
CriarAvulso_Sucesso_ProdutoIdNulo()
CriarVinculado_BackwardCompat_Sucesso()
```

**Application (unit-pinning da conversão R$→centavos no DTO):**
```csharp
// Pina o ×100 na projeção — não só o domínio
ListarCardapioPublico_AvulsoPreco35Reais_PrecoCentavos3500()
// Assere: dto.PrecoCentavos == 3500L quando item.PrecoEfetivo() == 35.00m

ListarCardapioPublico_AvulsoDisponivel_EstoqueAtualNulo()
ListarCardapioPublico_AvulsoComCategoria_AgrupaCorreto()
TenantNaoPodeAcessarStorefrontDeOutraEmpresa_Returns404()
```

**Integration:**
```csharp
GET_Menu_RetornaItemAvulso_ComNomePublico()
POST_ItemAvulsoNomeDuplicado_Returns400_ComMensagem()
OrderBy_MixAvulsoVinculado_NaoLancaException()
```

**Fixture `casa-da-baba/src/api/fixtures/menu-publico.json`:** adicionar item com `estoqueAtual: null` e `disponivel: true`. Smoke deve asserir que o frontend renderiza "Adicionar" (não "Esgotado").

---

## Arquivos críticos

| Arquivo | O que muda |
|---------|-----------|
| `EasyStock.Domain/Entities/Storefront/CardapioItem.cs` | ProdutoId `Guid?` + campos novos + `CriarAvulso()` |
| `EasyStock.Infra.Postgre/Data/Configurations/Storefront/CardapioItemConfiguration.cs` | Nullable + CHECK + índices CONCURRENTLY |
| `EasyStock.Infra.Postgre/Migrations/` | Nova migration (dois blocos) |
| `EasyStock.Infra.Postgre/Repositories/Storefront/CardapioItemRepository.cs` | null-filter + GetByIdAndScope |
| `EasyStock.Application/Common/HorarioBrasil.cs` | + `Agora()` |
| `EasyStock.Application/UseCases/Storefront/Menu/ListarCardapioPublicoUseCase.cs` | sort-key + EstoqueAtual nullable |
| `EasyStock.Application/UseCases/Admin/Storefront/Cardapio/AdicionarCardapioItemAdminUseCase.cs` | suporte avulso |
| `EasyStock.Admin/Pages/AdminPageBase.cs` | NivelAcesso.Admin + EmpresaId scope |
| `EasyStock.Admin/Pages/Storefronts/Cardapio/Create.cshtml(.cs)` | ProdutoId opcional + NomePublico + copy |
| `EasyStock.Admin/Pages/Storefronts/Cardapio/Index.cshtml(.cs)` | "Publicar" inline + seletor 1:N |
| `EasyStock.Api/Controllers/Storefront/MenuController.cs` | + endpoint imprimir |
| `C:\rep\casa-da-baba\src\api\fixtures\menu-publico.json` | item avulso com `estoqueAtual: null` |

---

## Consequências

**Fica mais fácil:**
- Thatiane gerencia o próprio cardápio sem depender do Felipe
- Onboarding de negócios de alimentação sem ERP obrigatório
- API pública reutilizável por apps e sites externos

**Fica mais difícil / atenção:**
- `ListarCardapioPublicoUseCase` tem lógica de resolução Nome/Categoria mais complexa — cobrir com testes unitários explícitos
- Migration em prod: verificar versão PG + medir volume antes de executar; usar CONCURRENTLY se volume > 100k rows
- AdminPageBase com dois níveis de acesso: testar isolamento (tenant A não acessa B)

**Não endereçado neste ADR (v2):**
- `TipoExibicao` em Storefront (Cardápio/Vitrine/Catálogo)
- Upload de fotos (R2/storage)
- Estoque real-time para vinculados (TODO pré-existente)
- TenantCardapioController separado em `/api/minha-vitrine/`
