# ADR-0038: Flag de dono do tenant (UsuarioEmpresa.IsOwner)

**Status:** Proposed
**Data:** 2026-06-28
**Escopo:** Onda 2 do refinamento UX do Admin (issue #730), item TEN-010.

## Contexto

TEN-010 (P2) quer destacar visualmente o "admin principal / dono" de cada tenant na aba
Usuarios do detalhe. Medindo o modelo: NAO existe flag de dono/criador em `UsuarioEmpresa`
nem em `Empresa`; existe apenas `NivelAcesso` (Admin / Gerente / Operador / Visualizador).

Derivar o dono como "o Admin mais antigo por data" em RUNTIME nao e confiavel (seeds,
duplicacoes, criacao por operacao podem furar a heuristica). Para um destaque que o suporte
vai usar como "com quem falar", o dado precisa ser estavel, nao adivinhado a cada render.

## Decisao

Adicionar `UsuarioEmpresa.IsOwner` (bool, default `false`), com backfill deterministico:
marca como dono o usuario `Admin` mais antigo por `CriadoEm` de cada tenant.

## Alternativas consideradas

| Opcao | Veredito |
|---|---|
| Derivar em runtime (1o Admin por data) | Rejeitada: nao confiavel; recalcula a cada render. |
| `Empresa.UsuarioDonoId` (FK no tenant) | Equivalente, porem mais invasiva (FK + navegacao). |
| `UsuarioEmpresa.IsOwner` (bool) | **Escolhida**: minima, fica no vinculo certo. |

## Ordem de aplicacao (boot-verde antes de qualquer query)

1. Migration adiciona a coluna `IsOwner` com default `false` (aditiva, nao destrutiva).
2. Deploy do schema (auto-aplicado no boot da Api).
3. Script de backfill marca 1 dono por tenant (Admin mais antigo por `CriadoEm`).
4. So entao a query/DTO (`TenantUsuarioInfo`) passa a projetar `IsOwner` e o FE renderiza
   o badge de dono.

## Consequencias

- 1 coluna nova; backfill unico.
- Tenants sem nenhum Admin (caso raro) ficam sem dono marcado: o FE trata como ausencia
  (sem badge), nao como erro.
- Isolamento: `IsOwner` vive em `UsuarioEmpresa`, ja escopado por `EmpresaId`; o filtro de
  tenant continua valendo (sem risco cross-tenant).

## Verificacao

- Boot da Api verde com a coluna recem-adicionada.
- Backfill marca exatamente 1 dono por tenant que tenha ao menos 1 Admin.
- Query/DTO le `IsOwner` sem erro; badge aparece so no dono.

## Itens de acao

1. [ ] Migration `IsOwner` (coluna nova, default false).
2. [ ] Script de backfill (Admin mais antigo por `CriadoEm`, 1 por tenant).
3. [ ] Projetar `IsOwner` em `TenantUsuarioInfo` + ordenar dono primeiro.
4. [ ] FE: badge de dono na aba Usuarios (reusa `<es-badge>`; icone `shield` do sprite).
