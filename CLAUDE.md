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

## 4. ESTADO CONHECIDO DO REPO

NOTA (#316): este bloco contem declaracoes ESTAVEIS (ADRs, decisoes irreversiveis,
organizacao de modulos). Estado volatil — contagem de issues, HEAD, ahead/behind,
working tree — NAO vive aqui: meça ao vivo no inicio de cada sessao (§0) e consulte
o board (§4.5). O snapshot abaixo e historico de referencia (2026-05-28 pos-policy-v3.0),
nao o estado atual.

Snapshot historico 2026-05-28: HEAD 14852aa0 (chore(policy): arquiva sistema ETK
conforme ADR-0022); master sincronizado; build verde (Husky roda arch tests);
working tree limpo; so branch master; so worktree C:/easy/EasyStok; sem stashes.

Sistema ETK (v2.1) arquivado em:
  - docs/tasks/_arquivo/2026-05-28-experimento-etk-superseded/
  - scripts/_arquivo/tasks-2026-05-28/
  - ADR-0020 marcado como Superseded por ADR-0022

Pendencias arquiteturais EM ABERTO: rastreadas no board GitHub (source-of-truth dinamico).
NAO ha contagem fixa aqui de proposito — qualquer numero hardcodado envelhece em horas
(ver #316). Consulte sempre ao vivo:
- Lista: https://github.com/michel-az-de/EasyStok/issues
- Board v2: https://github.com/users/michel-az-de/projects/1
- Total aberto: `gh issue list --state open --limit 200 --json number --jq 'length'`
- Por prioridade: `gh issue list --label priority:p0` (idem p1/p2/p3)
- Por modulo: `gh issue list --label priority:p1 --label caixa`
- Bugs: `gh issue list --search "bug in:title"`

Decisao Nfe* vs NotaFiscal* RESOLVIDA: ADR-0018 (Aceito, 2026-05-17).

Roadmap atual: docs/plan/README.md + docs/plan/nota-fiscal/00-README.md +
docs/plan/p-02-rotulagem-nutricional.md (ADR-0021).

## 4.5. TRACKING DE TRABALHO (GitHub Issues + Project board)

Source-of-truth do que esta sendo feito: **GitHub Issues** do repo
michel-az-de/EasyStok. Board visual: **GitHub Project v2** "EasyStok"
(criado em 2026-05-28 junto com a virada master-first).

URLs:
- Issues: https://github.com/michel-az-de/EasyStok/issues
- Project board v2: https://github.com/users/michel-az-de/projects/1

### Politica canonica (vinculante para todo agente)

**P1. Toda tarefa abre issue.** Sem isencao por tamanho. Typo de 1 linha,
refactor de 10 arquivos, plano arquitetural — tudo abre issue ANTES de
qualquer commit. Sem issue, sem trabalho. (Substitui a antiga isencao
de < 100 LoC / < 5 arquivos.)

**P2. Agente abre issue se nao existir.** Fluxo padrao:
  1. Felipe pede X.
  2. Agente busca: \`gh issue list --search "X"\` + lista de abertas relevantes.
  3. Se existe issue cobrindo X -> agente confirma com Felipe e usa essa.
  4. Se nao existe -> agente abre draft via \`gh issue create\` com:
     - Titulo imperativo curto (< 70 chars)
     - Body em markdown: Contexto + Escopo proposto + Entrega + Acceptance
     - Labels: modulo + prioridade
  5. **Agente aguarda aprovacao explicita do Felipe antes de tocar codigo.**

**P3. Status no board atualiza junto com o trabalho.**
  - Ao iniciar: agente comenta na issue "Doing — iniciado em \`<data>\`. Plano
    confirmado: ..." e move para \`Doing\` no Project board (manualmente ou
    via automation do board).
  - Ao fechar: commit com \`closes #N\` move automaticamente para \`Done\`.

**P4. Comentarios no decorrer (apenas nos 4 momentos abaixo — nao a cada commit):**
  - **Plano inicial confirmado** (1 comentario ao comecar).
  - **Decisao nao-obvia** (escolha entre 2+ opcoes com trade-off): registra
    opcoes consideradas + escolha + porque.
  - **Blocker / premissa refutada (R10):** medicao contradiz o que o plano
    supunha -> comenta + pede direcao.
  - **Fechamento** (1 comentario antes do \`closes #N\`): o que ficou pra
    outra issue (linkar), gotchas descobertos, proximos passos sugeridos.

**P5. Commit referencia a issue.** Mensagem obrigatoria:
\`tipo(escopo): descricao\\n\\ncloses #NNN\`
(ou \`refs #NNN\` em commits intermediarios de um trabalho fatiado).

### Comandos uteis

Filtros:
- \`gh issue list --label priority:p0\`
- \`gh issue list --label priority:p1 --label caixa\`
- \`gh issue list --search "X in:title"\`
- \`gh issue view N --comments\`

Criar issue (template basico):
  gh issue create --repo michel-az-de/EasyStok \\
    --title "..." --label "modulo,priority:pN" --body-file <path>

Comentar via arquivo (evita problemas de escape no shell):
  gh issue comment N --repo michel-az-de/EasyStok --body-file <path>

### Labels existentes

- Modulo: \`caixa\`, \`nfe\`, \`rotulagem\`, \`mobile\`, \`storefront\`, \`pwa\`,
  \`infra\`, \`web-api\`, \`domain\`, \`migrations\`
- Prioridade: \`priority:p0\` (bloqueador), \`priority:p1\` (alta),
  \`priority:p2\` (media), \`priority:p3\` (baixa)
- Default: \`bug\`, \`enhancement\`, \`documentation\`, etc.

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

## 7. COMPORTAMENTO DE DESENVOLVEDOR SENIOR

Todo agente que opera neste repositorio segue os 7 principios abaixo. VINCULANTE.

**PS1. Medir antes de afirmar (R10 reforcada).**
Toda afirmacao quantitativa, taxonomica ou de estado ("X e Y", "isto e trivial",
"Z nao usa isto", "N% disso e W") exige 1 comando de medicao imediatamente antes
(\`grep\`, \`git diff\`, \`git log\`, \`dotnet build\`, \`gh api\`, etc.). Sem
medicao, nao afirma — pergunta ou investiga primeiro.

**PS2. Root-cause antes de sintoma.**
Bug ou falha NAO e silenciado com try/catch generico, \`--no-verify\`, retry
loop, ou ignorar warning. Investiga ate a causa real ficar clara. Se a fix
paliativa e a escolha consciente (custo da fix real > custo do bug), o
trade-off vai documentado na issue.

**PS3. Recusa pedido ambiguo, pergunta antes (R13 reforcada).**
Quando o pedido tem 2+ interpretacoes razoaveis com consequencias diferentes,
pergunta — 1 decisao > 1 commit errado. Nunca "infere generosamente" e segue.
Pergunta UMA vez, decisiva — sem hesitacao serial.

**PS4. Fatia trabalho grande em commits build-verdes.**
Qualquer R5 (> 100 LoC / > 5 arquivos / breaking change / toca
Program.cs/migrations/Dockerfile/fly.toml) e fatiavel em sequencia de commits
build-verdes em master. Apresenta o plano de fatiamento, espera OK. Nada de
WIP entre commits.

**PS5. Registra trade-offs no lugar certo.**
Decisao com 2+ opcoes defensaveis -> justificativa vai na **issue** (ou ADR
se afetar arquitetura), nunca apenas na commit message. Commit message
reflete a decisao; issue/ADR explica por que.

**PS6. Self-review do plano antes de apresentar.**
Antes de cada plano significativo, agente roda mentalmente as 5 perguntas:
contradicao interna, premissas nao-medidas, capacidade realista, correlacao
entre fases, pior cenario. Se alguma falhar, refaz o plano antes de apresentar.

**PS7. Pausa quando estado contradiz premissa.**
Working tree, \`git log\`, build, ou output de comando contradiz o que foi
assumido no inicio -> PARA, reporta, espera direcao. Nunca reconcilia
silenciosamente. (Ver tambem R10 e PS1.)
