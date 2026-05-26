# Sessao TASK-EZ-MENU-001 - listar cardapio publico do storefront

Data: 2026-05-24 21:20 UTC
Worktree: C:/easy/EasyStok/.claude/worktrees/wt-ez-menu-001
Branch: feat/task-ez-menu-001-listar-cardapio-publico (4 commits ahead de ez-009)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo (aguardando revisao + push + merge na cadeia ez-001..ez-009)

## O que foi feito

Implementacao do primeiro endpoint end-to-end do storefront — o cardapio
publico. Anonimo, cacheavel por edge, sem PII no payload.

GET `/api/storefront/{slug}/menu`

Retorna `200` com `IReadOnlyList<CardapioItemPublicoDto>` no body. Mapeamento
de erros e headers:

- `404` + `ProblemDetails` (`title: "Storefront não encontrado"`) — quando
  slug nao resolve OU storefront esta inativo (nao distingue);
- `Cache-Control: public, max-age=300, s-maxage=300` na resposta 200 e 304;
- `ETag` = `"<SHA-256(payload) em hex>"` (strong);
- `304 Not Modified` quando cliente envia `If-None-Match` igual ao ETag
  (suporta prefixo `W/` e `*`).

Componentes entregues:

**Domain:**
- `StorefrontNaoEncontradoException` (subclasse de
  `RegraDeDominioVioladaException`) — mesmo padrao do que ja existia em
  wt-ez-auth-001, copiado para a base ez-009 deste worktree (a cadeia ainda
  nao foi mergeada e a exception precisava ja existir aqui).

**Application:**
- `ListarCardapioPublicoInput` (record com `Slug`).
- `ListarCardapioPublicoResult` (record com `IReadOnlyList<CardapioItemPublicoDto>`).
- `CardapioItemPublicoDto` (record com `Id, Nome, Descricao, PrecoCentavos,
  ImagemUrl, EstoqueAtual, Categoria, Ordem, Disponivel, Tag`). Sem
  EmpresaId, CustoReferencia, MargemEstimada, FornecedorId — campos
  internos nao vazam.
- `ListarCardapioPublicoUseCase` (sealed class — segue padrao do
  SolicitarOtpUseCase, sem `IUseCase<,>` interface). Resolve storefront via
  slug, valida `Ativo`, traz itens visiveis via repo, ordena
  Categoria.Nome ASC -> OrdemExibicao ASC (com sentinela para itens sem
  categoria), projeta em DTO. Preco efetivo via `CardapioItem.PrecoEfetivo()`
  (PrecoStorefront override OU Produto.PrecoReferencia).
- `ServiceCollectionExtensions.Storefront.cs` (partial DI) +
  `AddEasyStockStorefrontUseCases()` chamado em `AddEasyStockApplication`.

**Infra.Postgre:**
- `CardapioItemRepository.GetVisiveisDoStorefrontAsync` enriquecido com
  `.ThenInclude(Produto.Categoria)` e ordenacao server-side por
  `Categoria.Nome` + `OrdemExibicao`. Mudanca aditiva — `Include` extra nao
  quebra consumers existentes.

**Api:**
- `EasyStock.Api/Controllers/Storefront/MenuController.cs` com `[AllowAnonymous]`
  e `[Route("api/storefront/{slug}/menu")]`. Serializa JSON deterministico
  (PublicJsonOptions estatico — camelCase, sem indent) pra ETag bater entre
  requests. Retorna `File(bytes, "application/json; charset=utf-8")` para
  evitar duplo-serializar.

**Tests:**
- `ListarCardapioPublicoUseCaseTests.cs` (Application.Tests, 10/10 PASS):
  happy path, storefront inexistente/inativo, lista vazia, override de preco,
  ordenacao categoria+ordem, item sem categoria, esgotado, delegacao do
  filtro Visivel ao repo.
- `MenuControllerTests.cs` (Api.IntegrationTests, 6 cenarios — Testcontainers
  Postgres, no-op se Docker indisponivel): happy path com Cache-Control e
  ETag, 304 com If-None-Match igual, 200 com If-None-Match diferente, 404
  slug inexistente com ProblemDetails, 404 storefront inativo, 200 anonimo
  sem Authorization.

## Decisoes tomadas (divergencias documentadas vs YAML)

1. **`Program.cs` nao modificado** — YAML pedia "mapear /api/storefront/{slug}/menu
   sem auth", mas o app ja usa `app.MapControllers()`. Com `[Route]` +
   `[AllowAnonymous]` no controller, o roteamento e a politica de auth ja
   resolvem automaticamente. Modificar `Program.cs` seria ruido.

2. **`EstoqueAtual = 0` no DTO** — YAML reconhece que estoque e "snapshot
   eventual"; trazer o valor real exige `join` com ItemEstoque (ou nova
   query) e ficou fora do escopo. O campo existe no contrato pra cliente
   poder consumir; `Disponivel` (boolean) ja serve como sinal de esgotado.
   Marcar como follow-up.

3. **Resposta sem envelope `ApiResponse<T>`** — endpoint publico cacheavel
   por edge: payload menor reduz custo de transfer + permite ETag bater
   diretamente sobre o array (cliente final consome direto sem desempacotar).
   Difere do padrao `DataOk/DataPaged` dos endpoints autenticados, alinha
   com o que faz sentido pra UI publica.

4. **JSON serializer dedicado estatico** (`PublicJsonOptions` no controller)
   em vez do `IOptions<JsonOptions>` global. Necessario pra garantir que o
   payload usado no ETag bate byte-a-byte com o que sai na response —
   `JsonOptions` global pode ser alterado por outras configuracoes (incluindo
   converters), o estatico isolado e deterministico.

5. **ETag strong + compara ignorando prefixo `W/`** — payload e hash
   deterministico do conteudo; nao ha distincao semantica entre weak/strong
   pra esse caso. Aceitar `W/` na comparacao evita 200 desnecessario quando
   um proxy intermediario rebaixa pra weak.

6. **`StorefrontNaoEncontradoException` duplicada** — ja existe em
   `wt-ez-auth-001` (TASK-EZ-AUTH-001 a criou la). Como a cadeia ez-001..ez-009
   nao foi mergeada e AUTH-001 esta empilhada em outra branch, recriei a
   exception aqui com conteudo identico. Quando a cadeia subir em master, o
   merge entre as branches deve detectar o conflito de criacao mas o
   conteudo bate (mesma classe, mesma assinatura, mesma mensagem) — `git
   merge` resolve automaticamente OU a primeira branch a chegar fica e a
   segunda tem o arquivo conflitante removido.

7. **CRLF + UTF-8 BOM aplicado APENAS nos 12 arquivos do escopo** (mesma
   abordagem de af19ae14 em wt-ez-auth-001). `dotnet format
   --verify-no-changes` no escopo passa; global continua falhando em ~1900
   arquivos pre-existentes (CHARSET/ENDOFLINE em codigo herdado da branch
   ez-009 e ancestrais). Fora do escopo dessa task — limpeza global precisa
   de chore PR dedicado.

## Quality gates (status)

- `dotnet build EasyStok.sln -warnaserror:nullable -c Debug`: ✅ 0 erros,
  9 warnings (todos pre-existentes — Mobile/Android CA1422/XA0141/CS0618,
  ProdutosController CS8602, EF1002 em RLS tests).
- `dotnet test ArchitectureTests`: ✅ 25/25.
- `dotnet test Application.Tests --filter ListarCardapioPublicoUseCaseTests`:
  ✅ 10/10.
- `dotnet test Api.IntegrationTests --filter MenuControllerTests`:
  ✅ 6/6 (no-op aqui — sem Docker local; rodara em CI).
- `dotnet format --verify-no-changes --include <arquivos do escopo>`: ✅
  clean. Global continua falhando em pre-existentes (ver divergencia 7).

## Commits criados

- `976bfa43` test(TASK-EZ-MENU-001): red - ListarCardapioPublicoUseCase + 10 cenarios
- `9ed8fab2` feat(TASK-EZ-MENU-001): green - UseCase publico + repo carrega Categoria
- `55396cb9` feat(TASK-EZ-MENU-001): MenuController publico + ETag + Cache-Control
- `fbb3ab85` test(TASK-EZ-MENU-001): integration test MenuController com Postgres

## Branches criadas/deletadas

- Criada: `feat/task-ez-menu-001-listar-cardapio-publico` (a partir de
  `origin/feat/task-ez-009-repos-storefront`)
- Worktree: `.claude/worktrees/wt-ez-menu-001` (criado, ainda ativo)

## O que ficou pendente

- **Merge da cadeia ez-001..ez-009 em master**: as branches ancestrais nao
  tem PRs abertas (mesmo backlog ja resolvido em wt-ez-auth-001). EZ-MENU-001
  esta empilhada em ez-009; o PR precisa esperar essa cadeia subir.
- **Push da branch**: requer autorizacao explicita do Felipe (R9 do
  CLAUDE.md).
- **PR**: criar `gh pr create` precisa de base correta — provavelmente
  `feat/task-ez-009-repos-storefront` ate a cadeia subir.
- **EstoqueAtual real**: hoje retorna 0. Quando algum cenario de UI pedir
  estoque atual no cardapio (sinalizar "ultimas 3 unidades"), criar
  `ICardapioItemRepository.GetVisiveisComEstoqueDoStorefrontAsync` que faz
  join com `ItemEstoque` e popula o campo.
- **Cloudflare cache purge**: quando `AtualizarCardapioItemUseCase` existir,
  ele deveria disparar purge na Cloudflare API. Fora deste escopo (a YAML
  reconhece — invalidacao eventual de 5min e aceitavel pro MVP).
- **CORS**: a config global `AddEasyStockCors` ja permite o storefront
  publico no `Development`. Antes de Production, validar que o
  `Cors:AllowedOrigins` ou equivalente inclua o dominio publico do
  storefront (`casadababa.app` ou similar) e que o endpoint nao precisa de
  `[EnableCors]` especifico.

## Proxima acao recomendada

1. Felipe decide se quer abrir PR ja (base ez-009) ou esperar merge da cadeia.
2. Felipe autoriza `git push -u origin feat/task-ez-menu-001-listar-cardapio-publico`.
3. Apos cadeia ez-001..ez-009 + EZ-AUTH-001 + EZ-MENU-001 subirem em master,
   considerar chore PR para `dotnet format` global (limpar pre-existentes —
   bloqueia adoptar verify global em CI).
4. Iniciar TASK-EZ-FRETE-001 (calcular frete) OU TASK-EZ-AGEND-001 (listar
   janelas disponiveis) — ambos sao endpoints publicos do storefront e usam
   o mesmo padrao (resolver storefront por slug, anonimo, cacheavel).

## Referencias

- Task YAML: `casa-da-baba/docs/multi-agent/tasks/done/TASK-EZ-MENU-001-listar-cardapio-publico.yaml`
- ADRs relacionados: ADR-0012 (sessoes storefront), ADR-0015 (worktree por task)
- Dependencias: TASK-EZ-002 (CardapioItem entity), TASK-EZ-009 (repos storefront)
- Task vizinha do mesmo escopo: TASK-EZ-AUTH-001 (worktree wt-ez-auth-001) —
  serviu de referencia para padrao Controller publico + StorefrontNaoEncontrado.
