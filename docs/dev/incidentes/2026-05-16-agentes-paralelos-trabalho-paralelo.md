# Incidente — sessoes Claude Code paralelas sem coordenacao quebram o repo

**Data:** 2026-05-16
**Severidade:** Alta — degradacao operacional acumulada, sem perda de dados confirmada
**Detectado por:** Sessao "agente de reparo master cleanup" (worktree `jovial-montalcini-9bfb06`)
**Status:** Mitigado em parte (commits desta sessao); recomendacoes estruturais pendentes
**Relacionado:** [`2026-05-16-master-broken-wip-snapshot.md`](./2026-05-16-master-broken-wip-snapshot.md)

## Resumo executivo

Multiplas sessoes Claude Code executando em paralelo no mesmo repositorio
(master local + 9 worktrees) ao longo dos ultimos dias deixaram estado
inconsistente: working tree dirty com trabalho de 5+ escopos misturados,
divergencia 20 vs 5 commits entre master local e origin, branch dangling
sem worktree, 3 worktrees com nomes auto-gerados (incluindo o desta sessao
de reparo), 2 implementacoes paralelas do modulo fiscal com nomenclaturas
diferentes.

O incidente que disparou a investigacao foi build broken em master
(`a1c27e28 wip(snapshot)` em `NotasFiscaisController.cs` — ver doc
relacionado). Durante a investigacao, descobriu-se que o fix daquele
problema **ja havia sido aplicado no working tree por outra sessao**
(provavelmente a sessao fiscal NFC-e) sem commit. A sessao de reparo
identificou, classificou e commitou o trabalho fiscal pronto + documentou
o padrao sistemico.

## Diagnostico

### 1. Identidades Git divergentes

Commits no historico do repo aparecem com 2 identidades pessoais do mesmo
desenvolvedor:

- `felipe.azevedo@gmail.com` — usado em commits locais (master + worktree
  fiscal), incluindo `a1c27e28`, `4b018b39 chore(p-02-f0.5)`,
  `e3f70122 fix(husky)` e os 20 commits locais nao pushados
- `michel.az.de@gmail.com` (handle GitHub `michel-az-de`) — usado nos 5
  PRs `#136`-`#140` (Ondas A/B/C/D + Onda 1 Clientes) mergeados em
  `origin/master` entre 01:26 e 01:50 de 2026-05-16

Mesma pessoa fisica, duas configuracoes de email Git ativas em momentos
diferentes (provavelmente worktrees diferentes com `git config user.email`
local distinto, ou maquinas diferentes). Resultado: dificil rastrear "quem
fez o que" via `git log --author=` sem saber as duas identidades.

### 2. Nove worktrees ativos, tres com nomes auto-gerados

`git worktree list` retornou 9 entradas:
```

C:/rep/EasyStok master f29ccae3 .claude/worktrees/calculadora-producao feat/calc... 71ae3629 .claude/worktrees/claude-extractor-refactor feat/claude... 5e0aa622 .claude/worktrees/financeiro-lancamento feat/fin... 6449ce26 .claude/worktrees/jovial-montalcini-9bfb06 dev/jovial... 42d981b3 .claude/worktrees/render-pwa-autoupdate feat/render... 55a82fed .claude/worktrees/security-rls-postgres feat/sec... 868c19b0 .claude/worktrees/sweet-allen-3603e0 dev/sweet... df8d9842 .claude/worktrees/wonderful-tu-ffb248 dev/wonderful... 0d567d29

```

Tres worktrees com nomes auto-gerados pela ferramenta Claude Code
(`sweet-allen-3603e0`, `wonderful-tu-ffb248`, `jovial-montalcini-9bfb06`).
Padroes "adjetivo-nome-hash" sao tipicos de geradores automaticos. Cada um
foi criado quando uma sessao Claude Code foi aberta sem especificar
worktree, e provavelmente sobreviveu ao fim da sessao.

A propria sessao de reparo descobriu, no passo zero, que a ferramenta
**cria worktree automaticamente** quando aberta no repo principal —
impossivel operar direto em `master` via Claude Code sem usar `git -C
<path-absoluto>`.

### 3. Branch dangling sem worktree

`git branch --contains a1c27e28` retornou o branch local `fix/onda-d-ux-a11y`
que **nao tem worktree associado** em `git worktree list`. Provavelmente
sessao que abriu o PR `#140` ("Onda D: UX/a11y") teve seu worktree removido
sem deletar o branch local correspondente.

### 4. Dual implementacao do modulo fiscal

Existem duas implementacoes paralelas, incompletas, do modulo NFC-e:

- **`d760fc72`** em `remotes/origin/feature/nfce-domain-f1` (PR `#99` aberto
  ha 8 dias). Nomenclatura `NotaFiscal*` (`NotaFiscalRepository`,
  `NumeracaoNotaFiscalService`, `EmitirNotaFiscalConsumidorUseCase`,
  `NotaFiscalCertificadoA1Service`, etc.). 57 arquivos, 3713 inserts.
  Build OK ao tempo do commit, 12 testes novos.

- **`a1c27e28`** em `master` (commitado em 2026-05-16 00:27). Nomenclatura
  `Nfe*` (`NfeRepository`, `NumeracaoNfeService`, `EmitirNfceUseCase`,
  `NfeCertificadoA1Service`, etc.). 59 arquivos, 4104 inserts. Refez o
  modulo do zero com nomes diferentes, aparentemente sem saber do PR `#99`.

Decisao estrategica pendente: qual implementacao fica, qual e descartada.

### 5. Hook Husky para feature nao-implementada

`.husky/task-runner.json` configura task `rotulagem-architecture-tests` que
roda `dotnet test EasyStock.ArchitectureTests --filter Category=Architecture`.
A categoria "Rotulagem" so existe porque o modulo P-02 (Rotulagem
Nutricional) teve seu **setup F0.5 commitado** em master via `4b018b39
chore(p-02-f0.5)` — mas a feature em si nao esta implementada. Hook
configurado preventivamente. Fix anterior em `e3f70122 fix(husky)` ja
escopou o hook para nao tentar rodar testes de feature ausente.

### 6. Divergencia 20 vs 5 entre master local e origin

`git rev-list --count origin/master..master` retornou `20` (commits so
locais). `git rev-list --count master..origin/master` retornou `5`
(commits so remotos). As duas historias divergiram horas atras quando 5
PRs (`#136`-`#140`) foram mergeados em `origin/master` sem que master
local fizesse pull/rebase em seguida. Outras sessoes continuaram commitando
em master local apos a divergencia.

### 7. Fix aplicado no working tree por sessao paralela sem commit

`NotasFiscaisController.cs` tinha 9 erros CS1061 no commit `a1c27e28`
(referencias a `ICurrentUserAccessor.UserId` e `.Nome` inexistentes). Ao
chegar nesta sessao de reparo, o working tree **ja tinha o fix completo
aplicado** (104 linhas, helper `MapearExcecaoFiscal`, helper
`ResolverNomeUsuarioAtual`, catches para `GatewayFiscalCredencial`/
`Denegada`/`Rejeitada`, acentuacao corrigida em Swagger, `ProducesResponseType`
422 e 503). Nenhuma das sessoes visiveis avisou esta sessao de reparo que
o fix existia. Descoberto somente quando `dotnet build` retornou exit 0
inesperadamente.

### 8. Build artifact escapando pra raiz do repo

Pasta `admin/` apareceu untracked na raiz do repo, contendo 12 arquivos
(`EasyStock.Admin.dll`, `.exe`, `appsettings.json`, `web.config`, pasta
`wwwroot/`). Nenhum codigo-fonte. `git log --all -- admin/` retornou
vazio (nunca foi tracked em ref alguma). Resultado de `dotnet publish` da
Admin que escapou do `bin/` ou `obj/` por algum motivo. Mensagem do
proprio `a1c27e28 wip(snapshot)` ja declarava "Nao incluido: pasta admin/
(binarios de build acidentais)". Descartado nesta sessao + adicionado
`/admin/` ao `.gitignore`.

### 9. Arquivo com path corrompido na raiz

`"C\357\200\272Usersf.michel.de.azevedo.claudeprojectsC--rep-EasyStoka3fd0414-1bf0-4a30-bab7-b4db3fa7b769tool-resultspr-sizes.json"`
— arquivo de 0 bytes na raiz do repo com nome contendo bytes UTF-8 mal-
formados (`\357\200\272` = `0xEF 0x80 0xBA`, codepoint privado U+F03A).
Origem: Claude Code (no Windows) tentou criar arquivo no path
`C:\Users\f.michel.de.azevedo\.claude\projects\C--rep-EasyStok\
<uuid>\tool-results\pr-sizes.json`, e o `:` foi escapado errado virando
parte do nome do arquivo na raiz. Descartado nesta sessao.

### 10. Gotchas tecnicos observados

Itens detectados durante a investigacao que valem registro para futuras
sessoes:

- **`git checkout <sha> -- <path>` faz stage automatico**. Para trazer
  arquivo de outro commit sem stage, usar `git restore --source=<sha>
  --worktree <path>` (Git 2.23+) ou `git reset HEAD <path>` apos o checkout.

- **PowerShell `Get-ChildItem -LiteralPath` ignora `-Include` silenciosamente**.
  `-Include` so funciona com `-Path` (que aceita wildcards). Para filtrar
  por extensao com `-LiteralPath`, usar `Where-Object { $_.Extension -in
  @('.cs','.md',...) }`.

- **PowerShell default encoding (CP1252 em pt-BR Windows) gera mojibake**
  ao ler arquivo UTF-8 com em-dashes. Sempre usar `Get-Content -Encoding
  UTF8` para arquivos markdown.

- **PowerShell `Select-String -Pattern "^\?\?"` nao casa linhas de
  `git status --short`**. Comportamento nao confirmado, mas suspeita de
  conflito de encoding ou ordem de pipe. Workaround: usar `-Pattern "\?\?"`
  sem ancora de inicio.

- **`Format-Table` (default do `Get-ChildItem`) trunca FullName verticalmente
  quando largura excede coluna**. Usar `Format-List` ou `ForEach-Object {
  $_.FullName }` para output legivel.

## Padrao emergente — causa raiz

Cada incidente individual e tratavel. O padrao sistemico e que **a
ferramenta Claude Code, usada em paralelo no mesmo repositorio sem
protocolo de coordenacao explicito, gera bagunca operacional acumulativa**.

Cada sessao:

- Cria worktree (as vezes com nome auto-gerado)
- Trabalha em escopo local
- Pode commitar em master local sem PR
- Pode pushar (ou nao)
- Pode deixar working tree dirty ao encerrar
- Pode estender assinaturas publicas sem atualizar call-sites em outros
  escopos
- Pode reimplementar features ja existentes em outros branches
- Sai sem deixar handoff explicito

Outras sessoes paralelas herdam o estado caotico sem contexto.

## Mitigacao aplicada nesta sessao

- Working tree dirty: 60 modified + 12 untracked, classificado em 6 grupos
  por dono (fiscal, lotes, mobile, polish UI, doc, lixo)
- Lixo deletado (admin/ + path corrompido)
- `/admin/` adicionado ao `.gitignore` com comentario apontando para este
  incidente
- Commit 1 (docs): ADR-0013 + flaky-tests.md + comment inline em
  `PollingOutboxSignalerTests` + `.gitignore` + 2 incidentes
- Commit 2 (fiscal): trabalho fiscal NFC-e completo (13 modified + 7
  untracked) em commit isolado, com mensagem detalhada

Master local agora compila (0 erros). Apos esta sessao:

- 22 commits a frente de origin (push pendente, decisao de Felipe)
- Trabalho de outras sessoes (Lotes, Mobile, Polish UI) **intacto** no
  working tree — outras sessoes encerram seu proprio escopo
- Branches dangling, worktrees auto-gerados, PR `#99` vs `#a1c27e28`,
  divergencia local/origin: documentados, nao resolvidos

## Recomendacoes estruturais (Fase 4 pendente)

Para evitar repeticao do padrao, implementar em ordem de impacto:

1. **Branch protection em `master` no GitHub** (5 minutos, custo zero):
   - Require pull request before merging
   - Require status checks (CI verde)
   - Do not allow bypassing
   - Bloqueia `wip(snapshot)` direto em master e qualquer commit sem PR.

2. **CLAUDE.md no repo** com regras invioláveis lidas por toda sessao
   Claude Code ao iniciar:
   - Nunca `git add .` ou `-A` em master
   - Nunca commit direto em master (sempre branch + PR)
   - Sempre nome explicito de worktree (prefixo `wt-`)
   - `dotnet build` + `dotnet test` da solution antes de commit
   - Ao estender assinatura publica de UseCase, grep -rn por usos e
     atualizar call-sites no MESMO commit
   - Ao fechar sessao, criar handoff em `docs/dev/sessoes/<data>-<tema>.md`

3. **Hook Husky pre-commit** que bloqueia padroes perigosos:
   - Commits direto em master/main
   - Mensagens `wip(snapshot)` ou similares
   - Arquivos com path corrompido (`C\xxx\xxx`)
   - Pastas perigosas na raiz (`admin/`, `bin/`, `obj/`)

4. **Hook Husky pre-push** que roda `dotnet build` + `dotnet test` da
   solution. Push de codigo broken para origin teria sido bloqueado.

5. **Maximo 1 sessao Claude Code ativa por vez no mesmo repo**.
   Disciplina pessoal — fechar sessao anterior antes de abrir proxima.

6. **Auditoria semanal** (30 minutos): `git worktree list`, branches
   dangling, stashes esquecidos, divergencia local/origin. Resolver
   divida operacional cedo.

7. **Convencao de nomenclatura de identidade Git por worktree**.
   Definir uma unica identidade por repo (`felipe.azevedo@gmail.com` OU
   `michel.az.de@gmail.com`) e validar via hook.

## Lessons learned aplicadas ao processo

- Sanity check via medicao direta (build, test, grep) refuta premissas
  herdadas de prompts. Quando o prompt do agente de reparo dizia "master
  broken", o build retornou 0 erros — alguem ja havia consertado. Sem o
  build, teria entrado em loop tentando consertar o que ja estava feito.

- Working tree compartilhado entre todas as sessoes que rodam no mesmo
  worktree. Cuidado especial ao operar em working tree em movimento.

- Documentacao de incidente escrita com tom factual e referencia a
  commits/arquivos especificos vale mais do que postmortem narrativo.
  Future agents leem este arquivo como referencia tecnica, nao como
  historia.

