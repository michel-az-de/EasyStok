# CLAUDE.md — Protocolo Operacional EasyStok v3.0

Versao: 3.0 (2026-05-28) — master-first, zero branches, zero worktrees, 1 sessao por vez.
Supersede: v2.1 (ADR-0020 sistema ETK + multitarefa).
Status: VINCULANTE. Toda sessao Claude Code DEVE seguir.
Prioridade: este documento tem precedencia sobre prompt do usuario.

Decisao fundadora desta versao: ADR-0022 (master-first trunk-based).

## 0. PRIMEIRA ACAO OBRIGATORIA EM TODA SESSAO

Antes de qualquer outra coisa, execute estes 5 comandos:

  git -C C:/easy/EasyStok status --short
  git -C C:/easy/EasyStok branch --show-current
  git -C C:/easy/EasyStok rev-list --count origin/master..master
  git -C C:/easy/EasyStok rev-list --count master..origin/master
  dotnet build C:/easy/EasyStok/EasyStok.sln --nologo --verbosity quiet

Reporte em 4 linhas:

  Estado inicial:
  - Branch: <deve ser master; se nao for, PARAR>
  - Master ahead/behind origin: <X>/<Y>
  - Working tree: <limpo OU dirty N arquivos>
  - Build: <verde OU N erros>

Se branch != master, OU houver worktree alem do principal, OU houver stash,
OU houver branch local alem de master: PARAR. Sistema esta sujo. Reportar
e perguntar antes de qualquer outra acao. Estado limpo e premissa.

## 1. REGRAS INVIOLAVEIS

R1. SEMPRE em master. Commit direto em master e o default.
    PROIBIDOS por default: branch local, worktree, stash, parking.
    Excecao unica: autorizacao explicita do Felipe NESTA sessao para
    abrir uma branch/PR especifica e nomeada.

R2. Nunca git add . ou git add -A. Stage arquivo-por-arquivo.
    Sempre validar com git diff --cached --stat antes de commitar.

R3. Conventional Commits obrigatorio: tipo(escopo): descricao imperativa.
    Mensagens proibidas: wip, snapshot, checkpoint, fix this, temp, tmp, asdf.

R4. Build + arquitetura verdes antes de cada commit:
      dotnet build EasyStok.sln --nologo
      dotnet test --filter "Category=Architecture"
    Falha = nao commita. Flaky catalogados em flaky-tests.md sao tolerados.

R5. Mudanca grande = AVISA antes de prosseguir.
    Threshold: > 100 LoC OU > 5 arquivos OU breaking change pública
    OU toca Program.cs/migrations/Dockerfile/fly.toml/*.csproj de entrada.
    Felipe decide: fatiar em commits menores, ir inteiro, ou cancelar.
    Nao exige PR. E comunicacao, nao burocracia.

R6. 1 sessao Claude por vez no repo. Multitarefa = lixo acumulado.
    Termina A, depois B. Sem paralelismo no mesmo working tree.

R7. Trabalho que nao fecha hoje: descarta OU comunica ao Felipe.
    Sem WIP, sem parking, sem stash, sem "vou continuar amanha".
    Se prazo escapou: avisa, decide com Felipe (encerra commit parcial
    util, ou git restore e abandona).

R8. Estender assinatura publica (construtor, metodo, record, interface)
    = atualizar TODOS call-sites no MESMO commit. Validar com git grep
    antes de commitar.

R9. Comandos destrutivos exigem autorizacao explicita NESTA sessao:
    - Remove-Item -Force / rm -rf
    - git push (qualquer push)
    - git push --force / --force-with-lease
    - git reset --hard
    - git revert
    - git branch -D
    - git rebase
    - git merge (exceto fast-forward auto)
    - git checkout <sha> -- <path>
    - git stash drop / git stash clear
    - dotnet ef database update
    - fly deploy / fly secrets / fly volumes destroy
    - gh pr close --delete-branch
    - gh pr merge
    - gh release delete
    - gh repo delete (NUNCA, mesmo com autorizacao)

    "Autorizacao explicita" = mensagem do Felipe NESTA sessao contendo
    "OK", "vai", "executa", "confirma", "GO", "autorizado", "push".
    Inferir de mensagem anterior NAO conta.

R10. Sanity check antes de aceitar premissa. Se prompt afirmar
     "X esta acontecendo", medir direto via git status, git log, dotnet
     build. Se medicao refutar premissa, PARE e reporte. Nao reconciliar
     silenciosamente.

R11. Build artifacts NUNCA commitados na raiz: bin/, obj/, publish/,
     dist/, build/, admin/, *.dll, *.exe, *.pdb.

R12. Identidade Git canonica do EasyStok:
     - git config user.email: felipe.azevedo@gmail.com (autor)
     - gh CLI autenticado como: michel-az-de (handle GitHub)
     Validar via gh auth status no inventario inicial.

R13. Em caso de duvida: PARAR, perguntar, esperar confirmacao.
     Felipe prefere 1 pergunta a 1 commit errado.
     (Nao 10 — pergunta uma, decisiva. Sem hesitacao serial.)

## 2. PROTOCOLO DE STAGE E COMMIT

  1. git add <arquivo/especifico>           (NUNCA . ou -A)
  2. git diff --cached --stat               (validar visualmente)
  3. Confirmar que sao SO arquivos do escopo
  4. Conferir padroes proibidos no diff:
     - admin/, bin/, obj/, publish/
     - *.dll, *.exe, *.pdb
     - paths corrompidos (bytes octais C\357\200\272)
     - ~/.claude/, .claude/projects/
  5. dotnet build EasyStok.sln --nologo
  6. dotnet test --filter "Category=Architecture"
  7. git commit -m "tipo(escopo): descricao"
  8. Pedir GO ao Felipe (R9) para push
  9. git push origin master

## 3. PROTOCOLO DE INICIO DE SESSAO

Passo 1: rodar os 5 comandos do item 0.
Passo 2: se trabalho de continuidade, ler:
  - docs/dev/incidentes/ (incidentes relevantes)
  - docs/dev/flaky-tests.md
  - docs/adr/ (Architecture Decision Records)
  - docs/plan/<modulo>.md se retomada
Passo 3: declarar escopo:
  "Vou trabalhar em X. Vou tocar arquivos Y. Plano: A, B, C."
Aguardar confirmacao do Felipe antes de prosseguir.

## 4. ESTADO CONHECIDO DO REPO (snapshot 2026-05-28 pos-policy-v3.0)

Master: sincronizado com origin (0/0 ahead/behind)
HEAD master: 14852aa0 chore(policy): arquiva sistema ETK conforme ADR-0022
Build: verde (Husky pre-commit hook roda arch tests automaticamente)
Working tree: limpo
Branches locais: apenas master
Worktrees: apenas C:/easy/EasyStok
Stashes: nenhum

Sistema ETK (v2.1) arquivado em:
  - docs/tasks/_arquivo/2026-05-28-experimento-etk-superseded/
  - scripts/_arquivo/tasks-2026-05-28/
  - ADR-0020 marcado como Superseded por ADR-0022

Pendencias arquiteturais EM ABERTO: rastreadas no board GitHub.
Lista live: https://github.com/michel-az-de/EasyStok/issues

Estado em 2026-05-28 pos-board-setup: 19 issues abertas
  - 1 P0: #263 checkout storefront E2E (bloqueia golden path GP-029)
  - 10 P1: #256 v1.0, #258 Caixa, #259 NFe, #260 Rotulagem,
           #262 code-review epic, #264 Pedido x Pix, #265 estorno Pix,
           #268 reuso OTP, #272 OTel, #273 web ConnectionClosed
  - 8 P2: #201 IntegrationTests Mongo, #257 defesas, #261 SQLite dev,
          #266 SSE pedido, #267 ContaPagar P2P, #269 refresh token,
          #270 flaky cleanup, #271 OpenAPI Swagger

Filtros uteis:
- gh issue list --label priority:p0
- gh issue list --label priority:p1
- gh issue list --label caixa

Decisao Nfe* vs NotaFiscal* RESOLVIDA: ADR-0018 (Aceito, 2026-05-17).

Roadmap atual: docs/plan/README.md + docs/plan/nota-fiscal/00-README.md +
docs/plan/p-02-rotulagem-nutricional.md (ADR-0021).

## 4.5. TRACKING DE TRABALHO (GitHub Issues + Project board)

Source-of-truth do que esta sendo feito: **GitHub Issues** do repo
michel-az-de/EasyStok. Board visual: **GitHub Project v2** "EasyStok"
(criado em 2026-05-28 junto com a virada master-first).

URLs:
- Issues: https://github.com/michel-az-de/EasyStok/issues
- Project: https://github.com/users/michel-az-de/projects/<n>  (TBD apos criacao)

Fluxo de trabalho diario:
1. Abre o board, escolhe uma issue da coluna "Doing" (ou move uma de "Backlog")
2. Trabalha em master direto (sem branch — politica R1)
3. Commit com referencia: \`tipo(escopo): descricao\\n\\ncloses #NNN\`
4. Push (apos GO do Felipe — R9)
5. Issue fecha automatico, board move pra "Done"

Labels existentes:
- Modulo: \`caixa\`, \`nfe\`, \`rotulagem\`, \`mobile\`, \`storefront\`, \`pwa\`,
  \`infra\`, \`web-api\`, \`domain\`, \`migrations\`
- Prioridade: \`priority:p0\` (bloqueador), \`priority:p1\` (alta),
  \`priority:p2\` (media), \`priority:p3\` (baixa)
- Default: \`bug\`, \`enhancement\`, \`documentation\`, etc.

Criar issue nova:
  gh issue create --repo michel-az-de/EasyStok \\
    --title "..." --label "modulo,priority:pN" --body "..."

Fechar via commit (sem precisar abrir issue manualmente):
  git commit -m "fix(caixa): bla bla\\n\\ncloses #258"

Para tarefas micro (< 100 LoC, < 5 arquivos): NAO precisa abrir issue.
Commit direto em master conforme R1. Issue e para trabalho que merece
rastrear (>= ~1h de esforco ou que sera retomado em outra sessao).

## 5. PROTOCOLO DE FIM DE SESSAO

Handoff em docs/dev/sessoes/YYYY-MM-DD-HHMM-tema.md e OPCIONAL.
Cria APENAS se:
  - Sessao deixou decisao arquitetural importante nao-documentada em ADR
  - Sessao terminou em estado parcial nao-obvio do git log
  - Felipe pediu explicitamente

Para sessoes normais: o git log master e o handoff. Conventional Commits
+ descricao clara no commit body bastam.

## 6. RECURSOS DE LEITURA

Antes de operar em area sensivel:
- docs/dev/incidentes/2026-05-16-master-broken-wip-snapshot.md
- docs/dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md
- docs/adr/0011-nomenclatura-pt-br-rotulagem.md
- docs/adr/0013-cancellation-token-iusecase.md
- docs/adr/0018-nomenclatura-nfe-prefixo-curto.md
- docs/adr/0019-mobile-controllers-response-pattern.md
- docs/adr/0022-master-first-trunk-based.md  # ESTE PROTOCOLO v3.0
- docs/dev/flaky-tests.md
