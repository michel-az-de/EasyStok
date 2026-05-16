# Incidente — Master quebrado por commit wip(snapshot)

**Data:** 2026-05-16
**Severidade:** Alta — `origin/master` nao compila
**Detectado por:** Tentativa de merge do PR #135 (feat/calculadora-producao) em master
**Status:** Aberto — aguardando decisao do dono (Felipe)

## Resumo executivo

`origin/master` em 2026-05-16 nao compila por causa do commit
`a1c27e28 wip(snapshot): trabalho em andamento agente paralelo — Fiscal/NFC-e + Notificacoes + ResumoDia`.
O proprio prefixo `wip(snapshot)` na mensagem confirma que era trabalho
incompleto. O commit foi mergeado em master mesmo assim, quebrando o build
pra todo mundo que rebase a partir dali.

## Diagnostico

### Commit responsavel

```
a1c27e28 wip(snapshot): trabalho em andamento agente paralelo — Fiscal/NFC-e + Notificacoes + ResumoDia
```

### Arquivos afetados

- `EasyStock.Api/Controllers/NotasFiscaisController.cs` — 9 erros CS1061

### Erros concretos

```
NotasFiscaisController.cs(77,36): error CS1061: ICurrentUserAccessor nao contem definicao para UserId
NotasFiscaisController.cs(77,78): error CS1061: ICurrentUserAccessor nao contem definicao para UserId
NotasFiscaisController.cs(78,38): error CS1061: ICurrentUserAccessor nao contem definicao para Nome
NotasFiscaisController.cs(105,40): error CS1061: ICurrentUserAccessor nao contem definicao para UserId
NotasFiscaisController.cs(105,82): error CS1061: ICurrentUserAccessor nao contem definicao para UserId
NotasFiscaisController.cs(106,42): error CS1061: ICurrentUserAccessor nao contem definicao para Nome
NotasFiscaisController.cs(139,36): error CS1061: ICurrentUserAccessor nao contem definicao para UserId
NotasFiscaisController.cs(139,78): error CS1061: ICurrentUserAccessor nao contem definicao para UserId
NotasFiscaisController.cs(140,38): error CS1061: ICurrentUserAccessor nao contem definicao para Nome
```

### Causa raiz

`ICurrentUserAccessor` ([EasyStock.Application/Ports/Output/ICurrentUserAccessor.cs](EasyStock.Application/Ports/Output/ICurrentUserAccessor.cs))
expoe `UsuarioId : Guid` e nao tem propriedade `Nome`. O codigo novo em
`NotasFiscaisController.cs` referenciou nomes incorretos — provavelmente
hallucinacao de outra sessao do agente paralelo que nao validou contra a
interface real.

A propriedade `Nome` simplesmente nao existe na interface. Pra obter o nome
do usuario, o controller precisa injetar `IUsuarioRepository` e fazer
`GetByIdAsync(currentUser.EmpresaId, currentUser.UsuarioId)`.

## Impacto

- **Bloqueia merge de qualquer branch** que rebase a partir de master nao
  consigam compilar — o build inteiro quebra.
- **CI checks falham** ao processar PRs que partem de origin/master atual.
  No PR #135 tinha indicacao de `FAILURE` em "Build + dotnet test", "Test + Coverage Gate" — uma parte era billing,
  outra parte era esse build broken.
- **Deploy automatico ja efetuou push em master** sem o codigo compilar.
  Render workflow `deploy-render.yml` pode estar tendo falhas na pipeline.

## Decisao tomada

A definir — Felipe (dono) vai escolher entre:

1. **Reverter o commit `a1c27e28`** em master via `git revert a1c27e28` + push.
   Trabalho do agente paralelo precisa ser refeito em branch dedicada com
   build verde antes de mergear de novo.
2. **Fix forward**: novo commit em master corrigindo
   `NotasFiscaisController.cs` (trocar `UserId` por `UsuarioId`, remover ou
   substituir `Nome` por lookup via repo). Mantem o trabalho mas exige
   diagnostico cuidadoso porque o resto do snapshot WIP pode ter outros
   bugs latentes nao detectados pelo compilador.

## Regra pra evitar reincidencia

**Nenhum commit com prefixo "wip(...)" entra em master.** Snapshots de
agente paralelo devem ficar em branch propria ate compilar limpo e ter
testes passando. Use de stash/snapshot pra entregar trabalho parcial nao
e pratica aceitavel para branch principal.

Reforco da regra ja existente no projeto (auto-memory `feedback_commit.md`
+ `CLAUDE.md`): "Branch isolada por demanda — OBRIGATORIO". O fato de o
commit ter sido feito em master apesar da regra sugere que precisa de
hook ou check no CI rejeitando commits com `wip` no titulo direto em
master.

## Cronologia

- **2026-05-15 ~23h** — PR #135 (feat/calculadora-producao) criado, build verde isolado, 34/34 testes.
- **2026-05-16** — Felipe pede merge do PR #135 em master.
- **2026-05-16** — `gh pr merge 135 --squash` retorna `not mergeable: merge commit cannot be cleanly created`.
- **2026-05-16** — `git fetch origin master` revela 15+ commits novos incluindo `a1c27e28 wip(snapshot)`.
- **2026-05-16** — `git merge origin/master` na branch tem 4 conflitos (sw.js, csproj, MAUI bundle x2) — resolvidos.
- **2026-05-16** — `dotnet build EasyStock.Api` post-merge falha com 9 erros em `NotasFiscaisController.cs`. Mesmos erros aparecem em `origin/master` isolado.
- **2026-05-16** — Merge abortado (`git merge --abort`) pra preservar branch limpa. PR #135 segue aberto.
- **Acao planejada** — Dono arruma master (reverter ou fix forward), depois branch rebase + merge limpo.

## Estado da branch `feat/calculadora-producao` neste momento

- 9 commits em cima de `9271061` (master no momento que a branch foi criada).
- Build da branch isolado: 0 erros, 6 warnings preexistentes.
- 34/34 testes da feature verdes (16 Domain + 18 Application).
- Code review aplicado: 2 XSS criticos corrigidos no commit `89f35281`.
- PR #135 nao mergeado, aguardando master limpa.

## Anotacoes

- O snapshot WIP foi feito por outra sessao do agente em paralelo. Sem
  coordenacao entre sessoes — duas demandas mexendo na mesma area criou
  conflito.
- O CI tem job "PWA <-> MAUI Resources/Raw byte-identicidade" que falhou
  no PR #135 — nao era issue de bundle (SHA-256 conferido), foi billing
  do GitHub Actions impedindo o job de iniciar (annotation: "recent
  account payments have failed or your spending limit needs to be
  increased").
