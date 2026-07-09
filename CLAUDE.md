# CLAUDE.md — Protocolo Operacional EasyStok v4.0

Versao: 4.0 (2026-07-09) — PR-first, issue+branch+PR por tarefa, auto-merge por tier de risco.
Supersede: v3.1/v3.0 (master-first trunk-based) nos itens 0, R1, R5, R6, R7, R9, 2 e 4.5-P2.
Status: VINCULANTE. Toda sessao Claude Code DEVE seguir.
Prioridade: este documento tem precedencia sobre o prompt do usuario (exceto GO explicito NESTA sessao).
Decisao fundadora desta versao: ADR-0043 (adocao policy v4.0), que supersede ADR-0022 (master-first).

> **Nota de honestidade:** aqui autor = revisor = merger; o PR-sempre adiciona **auditabilidade e higiene**
> (revert granular, trilha, review async), NAO seguranca independente. O gate real de correcao e o
> **tier ALTO + aprovacao humana** (label `aprovado`). Nao confundir presenca (CI verde) com corretude.

## OVERRIDE DO REPO

- REPO_SLUG:        michel-az-de/EasyStok
- TRUNK:            master        (sempre AUTO-DETECTAR: `git symbolic-ref --short refs/remotes/origin/HEAD`)
- STACK:            .NET 8 (EF Core, EasyStock.* multi-projeto, MAUI Android, Blazor/Web)
- BUILD_CHECK:      powershell -File scripts/poka-yoke/gate.ps1   (build + arch-tests sem rebuild, ADR-0040; = Husky pre-commit)
- TEST_ARCH:        incluido no gate.ps1
- GIT_EMAIL:        michel.az.de@gmail.com   (email vinculado a conta -> atribui os commits)
- GH_ACCOUNT:       michel-az-de
- AUTO_MERGE_TIER:  baixo = chore/docs/test/fix-trivial (auto no verde); alto = feat/refactor/migracao/auth/RLS/policy (aguarda label `aprovado`)
- HAS_CI:           sim (.github/workflows/ci.yml e outros)
- LABELS_MODULO:    caixa, nfe, rotulagem, mobile, storefront, pwa, infra, web-api, domain, migrations, admin, web
- LABELS_PRIO:      priority:p0..p3
- ADR_ADOCAO:       docs/adr/0043-adocao-policy-v4.md

## 0. PRIMEIRA ACAO OBRIGATORIA EM TODA SESSAO

Com **cwd = raiz do repo** e **git puro** (NUNCA `git -C`, negado nesta maquina), rodar os 4 comandos git
(sincronos) e lancar o gate/build EM BACKGROUND (nao bloqueie o inventario esperando — issue 814):

  git status --short
  git branch --show-current
  git rev-list --count origin/master..master
  git rev-list --count master..origin/master
  powershell -File scripts/poka-yoke/build-check.ps1   # BACKGROUND

Reporte em ate 5 linhas: branch atual (esperado: `master` em sessao limpa, OU a branch da tarefa em andamento);
master ahead/behind; working tree (limpo | dirty N); worktrees extras; build (verde | N erros | "verificando em background").

Regras do build em background: reportar o resultado assim que chegar; PROIBIDO commitar antes do gate reportar
verde (o `gate.ps1` do pre-commit garante isso mecanicamente de qualquer forma); sessao somente-leitura pode pular.

**Definicao de SUJO (v4.0) e o que fazer:**
- Mudanca nao-commitada que NAO pertence a uma tarefa ativa -> **PARE (STOP duro)**. Reporte e pergunte;
  nao reconcilie nem descarte sozinho. Estado limpo e premissa.
- Branch `feat|fix|chore/*` orfa (issue fechada / PR mergeado) ou worktree orfao -> pode **OFERECER** cleanup
  nao-destrutivo (`git branch -d` so se merged; `git worktree remove` so se limpa). `-D`/`reset --hard`/descartar
  nao-commitado exigem GO (R9).
- Worktrees novas vao para `C:\rep\.worktrees\<repo>\<slug>` (fora do repo). As antigas em `.claude/worktrees/`
  do harness sao esperadas. Inventario seguro: `scripts/poka-yoke/worktree-status.ps1` (ADR-0029).

## 1. REGRAS INVIOLAVEIS

R1 (v4.0 FLIP). Toda tarefa vive numa BRANCH. Nada de commit direto no master (exceto §HOTFIX autorizado).
   Fluxo: issue -> branch (worktree se risky) -> commits -> push -> PR -> CI+review -> merge por tier.

R2 (mantida). Nunca `git add .` / `git add -A`. Stage arquivo-por-arquivo; validar `git diff --cached --stat`.

R3 (mantida). Conventional Commits: `tipo(escopo): descricao imperativa`. Proibido: wip, snapshot, checkpoint,
   fix this, temp, tmp, asdf. Corpo referencia a issue (`refs #N`; `closes #N` no PR/commit final).

R4 (mantida). Build + arquitetura verdes antes de cada commit:
     powershell -File scripts/poka-yoke/gate.ps1   # build + arch-tests sem rebuild (ADR-0040)
   E o MESMO comando que o Husky pre-commit roda; rodar antes so antecipa o veredito. Falha = nao commita.
   Flaky catalogados em flaky-tests.md sao tolerados. No PR, o CI repete o gate e destrava o auto-merge (tier baixo).

R5 (v4.0 FLIP). PR SEMPRE. Merge somente via PR. Nao existe "isento de PR". Mudanca grande
   (> 100 LoC OU > 5 arquivos OU breaking OU toca Program.cs/migrations/Dockerfile/fly.toml/*.csproj de entrada)
   NAO cancela o PR: fatia em commits menores dentro da branch e explica o racional no PR (e e tier ALTO).

R6 (v4.0 MODIFY). Default: 1 branch-in-place por working tree. Paralelismo/tarefa longa/arriscada -> worktree
   isolado em `C:\rep\.worktrees\<repo>\<slug>`. Cada worktree = 1 tarefa = 1 branch = 1 issue.

R7 (v4.0 FLIP). Trabalho inacabado NAO e descartado: persiste na branch + issue aberta (continuidade real).
   Proibido apenas master sujo e commit-lixo. A branch versionada e a memoria; sem stash como memoria.

R8 (mantida). Estender assinatura publica = atualizar TODOS os call-sites no MESMO commit (`git grep` antes).

R9 (v4.0 MODIFY, tiered). A standing policy PRE-AUTORIZA, como fluxo normal e sem GO: `git push` da branch de
   tarefa; e `gh pr merge --squash --delete-branch` quando CI + review verdes (tier BAIXO). Exigem GO explicito
   NESTA sessao: `git push --force`/`--force-with-lease`, `git reset --hard`, `git rebase` de historia publicada,
   `git branch -D` de branch alheia/nao-mergeada, `git revert` no master, `Remove-Item -Force`/`rm -rf` fora de
   artefatos, `dotnet ef database update`, `fly deploy/secrets/volumes destroy`, `gh release delete`.
   NUNCA (mesmo com GO): `gh repo delete`. "GO" = mensagem do Felipe NESTA sessao ("OK/vai/executa/confirma/GO/autorizado").

R10 (mantida). Sanity check antes de aceitar premissa: medir via git/build/gh. Se refutar, PARE e reporte.

R11 (mantida). Build artifacts nunca commitados na raiz: bin/, obj/, publish/, dist/, build/, admin/, *.dll, *.exe, *.pdb.

R12 (v4.0 MODIFY). Identidade Git canonica do EasyStok:
     - git config user.email: michel.az.de@gmail.com (autor; email VINCULADO a conta -> atribui no GitHub)
     - gh CLI autenticado como: michel-az-de
     Validar via `gh auth status` no inventario inicial.

R13 (mantida). Em duvida genuina: PARE, pergunte UMA vez, decisiva. Nao conflita com o async: tarefa clara segue.

R14 (mantida). Comunicacao SEMPRE em pt-BR com o Felipe. Codigo/identificadores/commits seguem o padrao do repo.

## 2. CICLO DE VIDA DA TAREFA (stage, commit, PR, merge)

  1. ISSUE (`gh issue create`, NAO-bloqueante) -- titulo imperativo; body Contexto/Escopo/Aceite (checkboxes); labels modulo+prioridade.
  2. BRANCH `<tipo>/<slug>-<N>` a partir do master atualizado. Worktree em `C:\rep\.worktrees\...` se risky.
  3. STAGE arquivo-a-arquivo (R2): `git add <arquivo>` -> `git diff --cached --stat` -> conferir paths proibidos
     (admin/, bin/, obj/, publish/; *.dll/*.exe/*.pdb; ~/.claude/; .claude/projects/).
  4. GATE: `powershell -File scripts/poka-yoke/gate.ps1` verde (o Husky pre-commit roda o mesmo no commit).
  5. COMMIT `git commit -m "tipo(escopo): descricao" -m "refs #N"`.
  6. PUSH `git push -u origin HEAD` (pre-autorizado, R9).
  7. PR `gh pr create --base master --title "tipo(escopo): descricao"` (titulo = msg do squash) + body `closes #N`.
  8. GATE do PR: detectar checks (`gh pr view --json statusCheckRollup`); ha CI (ci.yml) -> `gh pr checks --watch`.
     Review automatizado: `/code-review` + `pr-review-toolkit:review-pr`. Aceite da issue todo marcado.
  9. MERGE por tier: baixo + verde -> `git switch master && git pull --ff-only`, tree limpo, `gh pr merge --squash --delete-branch`.
     Alto (feat/refactor/migracao/auth/RLS/policy) -> PR aberto ate label `aprovado`.
 10. CLEANUP: worktree remove+prune; branch local `-d`; `commit-commands:clean_gone`. Depois DoD "zero resquicios".

Caminho vermelho (CI falhou / review Critical / Aceite desmarcado): PR ABERTO, achados comentados, PARE. Nunca mergeia.

## §HOTFIX (excecao ao PR-first)

Commit direto no master SOMENTE quando: (a) urgente (producao quebrada/bloqueio critico) E (b) GO explicito NESTA
sessao. Mesmo assim: aplica R2/R3/R4 (gate verde); abre issue post-hoc (label incident/hotfix, referenciando o SHA);
vigia o CI do master (escape `git revert`). Sem GO, hotfix vira tarefa normal. Ver comando `/hotfix`.

## §DEFINITION OF DONE — "zero resquicios"

Asseverar por exit code/JSON (nao por texto): Aceite todo marcado; issue CLOSED (`closes #N`); PR MERGED (squash);
branch remota e local removidas; worktree removido + prune; CI verde no master pos-merge; CHANGELOG atualizado;
ADR criado se houve decisao; working tree limpo sem artefatos (R11).

## 3. PROTOCOLO DE INICIO DE SESSAO

Passo 1: rodar os 4 comandos git do item 0 + build-check em background.
Passo 2: se trabalho de continuidade, ler: docs/dev/incidentes/, docs/dev/flaky-tests.md, docs/adr/, docs/plan/<modulo>.md.
Passo 3: declarar escopo ("Vou trabalhar em X, tocar Y, plano A/B/C") -- a issue e o registro; o fluxo e nao-bloqueante.

## 4. ESTADO CONHECIDO DO REPO

NOTA (#316): este bloco contem declaracoes ESTAVEIS (ADRs, decisoes irreversiveis, organizacao de modulos).
Estado volatil (contagem de issues, HEAD, ahead/behind, working tree) NAO vive aqui: meca ao vivo no §0 e consulte o board.

Sistema ETK (v2.1) arquivado (docs/tasks/_arquivo/2026-05-28-experimento-etk-superseded/; ADR-0020 Superseded por ADR-0022).
Decisao Nfe* vs NotaFiscal* RESOLVIDA: ADR-0018. Roadmap: docs/plan/README.md + docs/plan/nota-fiscal/00-README.md +
docs/plan/p-02-rotulagem-nutricional.md (ADR-0021). Pendencias arquiteturais: no board GitHub (source-of-truth dinamico).

## 4.5. TRACKING DE TRABALHO (GitHub Issues + Project board)

Source-of-truth: **GitHub Issues** de michel-az-de/EasyStok. Board: **GitHub Project v2** "EasyStok".
- Issues: https://github.com/michel-az-de/EasyStok/issues
- Board v2: https://github.com/users/michel-az-de/projects/1
- Total aberto: `gh issue list --state open --limit 200 --json number --jq 'length'`; por prioridade/modulo: `gh issue list --label ...`.

### Politica canonica (vinculante)

**P1. Toda tarefa abre issue.** Sem isencao por tamanho. Sem issue, sem trabalho.
**P2 (v4.0 FLIP). NAO-bloqueante.** Abre a issue (Contexto/Escopo/Aceite + labels) e **prossegue imediatamente**;
   o Felipe acompanha async pela trilha issue/PR. (A antiga espera-de-aprovacao-antes-de-codar foi removida;
   a aprovacao migrou para o MERGE do tier ALTO.)
**P3.** `closes #N` no merge fecha a issue e move para Done no board.
**P4.** Comentarios nos momentos-chave: plano inicial; decisao nao-obvia (opcoes+escolha+porque); blocker/premissa
   refutada (R10); fechamento (o que ficou pra outra issue, gotchas, proximos passos).
**P5.** Commit/PR referencia a issue (`refs #N` intermediario; `closes #N` no PR).

Labels: modulo (`caixa`,`nfe`,`rotulagem`,`mobile`,`storefront`,`pwa`,`infra`,`web-api`,`domain`,`migrations`,`admin`,`web`)
+ prioridade (`priority:p0..p3`) + default (`bug`,`enhancement`,`documentation`). Comentar via `--body-file` (evita escape).

## 5. PROTOCOLO DE FIM DE SESSAO

Handoff em docs/dev/sessoes/YYYY-MM-DD-HHMM-tema.md e OPCIONAL. A branch + issue + o PR ja sao a memoria duravel;
crie handoff so se houver decisao arquitetural nao-documentada em ADR, estado parcial nao-obvio, ou pedido explicito.

## 6. RECURSOS DE LEITURA

Antes de operar em area sensivel:
- docs/dev/incidentes/2026-05-16-master-broken-wip-snapshot.md
- docs/dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md
- docs/adr/0011-nomenclatura-pt-br-rotulagem.md
- docs/adr/0013-cancellation-token-iusecase.md
- docs/adr/0018-nomenclatura-nfe-prefixo-curto.md
- docs/adr/0019-mobile-controllers-response-pattern.md
- docs/adr/0022-master-first-trunk-based.md   # superseded pela adocao v4.0 (ADR-0043)
- docs/adr/0040-gate-unico-pre-commit.md
- docs/adr/0043-adocao-policy-v4.md            # ESTE PROTOCOLO v4.0
- docs/dev/flaky-tests.md
- docs/plan/consolidacao-v1/README.md          # diagnostico canonico da Consolidacao v1 (epico #786)

## 7. COMPORTAMENTO DE DESENVOLVEDOR SENIOR (VINCULANTE)

**PS1. Medir antes de afirmar (R10 reforcada).** Toda afirmacao quantitativa/de estado exige 1 comando de medicao antes.
**PS2. Root-cause antes de sintoma.** Nada de silenciar com try/catch generico, `--no-verify`, retry loop. Fix paliativa consciente -> trade-off na issue.
**PS3. Recusa pedido ambiguo, pergunta antes (R13).** 2+ interpretacoes com consequencias -> pergunta UMA vez, decisiva.
**PS4. Fatia trabalho grande em commits verdes DENTRO da branch.** Apresenta o plano de fatiamento; nada de WIP no master.
**PS5. Registra trade-offs no lugar certo.** Decisao com 2+ opcoes -> justificativa na issue (ou ADR se arquitetural), nao so no commit.
**PS6. Self-review do plano antes de apresentar.** Roda as 5 perguntas (contradicao, premissas nao-medidas, capacidade, correlacao, pior cenario).
**PS7. Pausa quando estado contradiz premissa.** working tree/git log/build contradiz o assumido -> PARA, reporta, espera direcao.
