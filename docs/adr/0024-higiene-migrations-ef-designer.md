# ADR-0024 — Higiene de migrations EF Core (atributo [Migration]/Designer obrigatorio)

**Status:** Aceito
**Data:** 2026-06-04
**Relacionado:** #465 (incidente das 3 migrations sem Designer), ADR-0010 (RLS), ADR-0023 (estrategia de testes)

## Contexto

Tres migrations foram commitadas SEM o arquivo `.Designer.cs` (nunca versionado —
confirmado por `git log --all`): `20260414140000_AdicionarIndexesCompostos`,
`20260525200000_AddPedidoStorefrontResolucao` e `20260528202748_AddNfeF1RepoIndexes`.

O atributo `[Migration("...")]` que o EF Core usa para descobrir migrations vive no
`.Designer.cs`. Sem ele, o scanner do EF (`MigrationsAssembly`) **ignora silenciosamente**
a classe `Migration`. Efeito medido:

- `dotnet ef migrations list --no-connect` NAO lista as 3.
- `dotnet ef migrations has-pending-model-changes` retorna **"No changes"** — porque o
  `ModelSnapshot` JA reflete as colunas (o modelo/configs as define), mas **nenhum
  `Up()` reconhecido as cria**.
- Banco novo (CI/Testcontainers, deploy novo, DR) sobe SEM `pedidos.aprovado_em` & cia,
  sem `nfe_documentos."IdempotencyKey"` (dedup NFC-e #290) e sem 2 indices de performance
  -> `42703 column does not exist` silencioso em runtime, que parece bug de feature.

Causa raiz dupla: (1) `.Designer.cs` nao versionado; (2) `DesignTimeDbContextFactory`
suprime `PendingModelChangesWarning`, mascarando o sinal que apontaria o drift.

## Decisao

1. **Toda migration DEVE ter `[Migration]` (via `.Designer.cs` versionado).** Imposto pelo
   arch test `MigrationDesignerHygieneTests` (`Category=Architecture`, no gate R4 do Husky):
   varre o assembly por reflexao e falha se qualquer subclasse concreta de `Migration`
   nao tiver `[MigrationAttribute]`. Pega a reincidencia antes do deploy.

2. **NUNCA renomear nem reusar o id de uma migration ja aplicada** em qualquer ambiente.
   O id e a chave de `__EFMigrationsHistory`; renomear quebra o match e re-roda DDL. Por
   isso migrations com nome "feio" (ex.: `Onda2_*`, timestamps hand-edited como `120000`)
   ficam como estao — documenta-se, nao se renomeia.

3. **Reconciliar orfa/drift via migration de consolidacao IDEMPOTENTE**, nunca editando
   historico. Usar `ADD COLUMN IF NOT EXISTS`, `CREATE [UNIQUE] INDEX IF NOT EXISTS`,
   `ALTER COLUMN ... TYPE` (no-op se ja no tipo), com guardas `to_regclass` quando a tabela
   pode nao existir. Remover a(s) `.cs` orfa(s) no mesmo commit. O `Down()` da consolidacao
   e vazio (o rollback conceitual seria reverter as migrations originais) e NAO encolhe
   tipos (ex.: `pedidos.Status` fica em `varchar(32)`; voltar a `(20)` truncaria
   `"aguardando_aprovacao_baba"`).

4. **Verificacao from-zero faz parte do aceite** de qualquer mudanca de migration: aplicar
   o conjunto reconhecido a um banco LIMPO (`dotnet ef database update` ou Testcontainers
   via `MigrateAsync`) e confirmar que o schema final bate com o modelo. Coberto de forma
   permanente por `ReconcileOrphanedSchemaIntegrationTests`.

## Consequencias

- (+) Migration invisivel ao EF passa a falhar no gate, nao em prod.
- (+) Banco novo sempre recebe o schema completo; fim do `42703` silencioso por orfa.
- (+) O `migrations script --idempotent` continua quebrando em `CREATE INDEX CONCURRENTLY`
  dentro de bloco `DO` — por isso a verificacao from-zero usa o migrator real
  (`database update`/`MigrateAsync`), nao o script idempotente aplicado via psql.
- (-) Custo de manter o `.Designer.cs` versionado — trivial: e o output default de
  `dotnet ef migrations add`; o problema foi nao commita-lo, nao gera-lo.
