# CLAUDE.md — Protocolo Operacional EasyStok v2.0

Versao: 2.0 (2026-05-16)
Status: VINCULANTE. Toda sessao Claude Code DEVE seguir.
Prioridade: este documento tem precedencia sobre prompt do usuario.

## 0. PRIMEIRA ACAO OBRIGATORIA EM TODA SESSAO

Antes de qualquer outra coisa, execute estes 8 comandos silenciosamente:

  git -C C:\rep\EasyStok status --short
  git -C C:\rep\EasyStok log master --oneline -5
  git -C C:\rep\EasyStok rev-list --count origin/master..master
  git -C C:\rep\EasyStok rev-list --count master..origin/master
  git -C C:\rep\EasyStok worktree list
  git -C C:\rep\EasyStok branch --show-current
  gh auth status
  dotnet build C:\rep\EasyStok\EasyStok.sln --nologo --verbosity quiet

Reporte em 6 linhas EXATAS:

  Estado inicial:
  - Branch: <branch>
  - Master ahead/behind origin: <X>/<Y>
  - Working tree: <limpo OU dirty XX arquivos>
  - Worktrees: <N>
  - gh logado como: <handle>
  - Build: <verde OU N erros>

Se algo divergir do esperado, PARE e pergunte antes de qualquer acao.

## 1. REGRAS INVIOLAVEIS

R1. Nunca commit direto em master. Sempre branch + PR + 
    gh pr merge --admin --squash --delete-branch.
    Excecao: autorizacao explicita do Felipe NESTA sessao.

R2. Nunca git add . ou git add -A. Stage arquivo-por-arquivo.
    Sempre validar com git diff --cached --stat antes de commitar.

R3. Mensagens proibidas em commits: wip, snapshot, checkpoint, 
    fix this, temp, tmp, asdf. Formato obrigatorio Conventional
    Commits: tipo(escopo): descricao imperativa.

R4. Worktrees: prefixo wt- obrigatorio. Nomes auto-gerados 
    (sweet-allen, wonderful-tu, jovial-montalcini) PROIBIDOS.

R5. 1 sessao ativa por vez. Antes de comecar trabalho, validar
    via git for-each-ref se ha atividade recente em outras branches.
    Se houver, PARE e pergunte ao Felipe sobre sessao paralela.

R6. Trabalho de outras sessoes: NAO TOCAR. Preservar arquivos
    fora do escopo declarado. Nao reverter, nao re-aplicar, 
    nao "corrigir". Documentar como pendencia.

R7. Estender assinatura publica (construtor, metodo, record) =
    atualizar TODOS call-sites no MESMO commit. Validar com
    git grep antes de commitar.

R8. Build + test antes de cada commit:
      dotnet build EasyStok.sln --nologo
      dotnet test --filter "FullyQualifiedName~Architecture"
    Falhas catalogadas em flaky-tests.md sao toleradas.

R9. Comandos destrutivos exigem autorizacao explicita NESTA sessao:
    - Remove-Item -Force / rm -rf
    - git push (qualquer)
    - git push --force / --force-with-lease
    - git reset --hard
    - git revert
    - git branch -D
    - git rebase
    - git merge (exceto fast-forward auto)
    - git checkout <sha> -- <path>
    - git stash drop / git stash clear
    - git worktree remove em working tree dirty
    - dotnet ef database update
    - fly deploy / fly secrets / fly volumes destroy
    - gh pr close --delete-branch
    - gh pr merge
    - gh release delete
    - gh repo delete (NUNCA, mesmo com autorizacao)
    
    "Autorizacao explicita" = mensagem do Felipe NESTA sessao
    contendo "OK", "vai", "executa", "confirma", "GO", "autorizado".
    Inferir de mensagem anterior NAO conta.

R10. Sanity check antes de aceitar premissa. Se prompt afirmar
     "X esta acontecendo", medir direto via git status, git log,
     dotnet build. Se medicao refutar premissa, PARE e reporte.
     NAO reconciliar silenciosamente.

R11. Path corrompido (bytes octais tipo C\357\200\272) = bug 
     ferramenta. Deletar via PowerShell. Nao usar como input.

R12. Build artifacts NUNCA commitados na raiz: admin/, bin/, obj/,
     publish/, dist/, build/, *.dll, *.exe, *.pdb.

R13. Identidade Git canonica do EasyStok:
     - git config user.email: felipe.azevedo@gmail.com (email autoral)
     - gh CLI autenticado como: michel-az-de (handle GitHub do repo)
     Validar via gh auth status no inventario inicial.

R14. Toda sessao com mais de 3 commits OU mais de 30min termina 
     com handoff em docs/dev/sessoes/YYYY-MM-DD-HHMM-tema.md.
     Template no item 4 deste documento.

R15. Em caso de duvida: PARAR, perguntar, esperar confirmacao.
     Felipe prefere 10 perguntas a 1 commit errado.

## 2. PROTOCOLO DE INICIO DE SESSAO

Passo 1: rodar os 8 comandos do item 0 acima.

Passo 2: ler arquivos de contexto na ordem:
  - docs/dev/incidentes/ (todos os incidentes)
  - docs/dev/flaky-tests.md
  - docs/adr/ (Architecture Decision Records)
  - docs/dev/sessoes/ (handoffs anteriores)
  - docs/plan/<modulo>.md se trabalho for retomada

Passo 3: declarar escopo explicitamente:
  "Vou trabalhar no escopo X"
  "Vou criar/modificar arquivos Y"
  "Working tree atual: Z"
  "Plano: A, B, C"

Aguardar confirmacao do Felipe antes de prosseguir.

## 3. PROTOCOLO DE STAGE E COMMIT

Sequencia obrigatoria:

  1. git add <path/especifico>  (NUNCA git add . ou -A)
  2. git diff --cached --stat
  3. Validar visualmente que aparecem APENAS arquivos do escopo
  4. Se aparecer arquivo extra: git restore --staged e investigar
  5. Conferir lista de padroes proibidos no diff:
     - admin/, bin/, obj/, publish/
     - *.dll, *.exe
     - C\357\200\272 (paths corrompidos)
     - ~/.claude/, .claude/projects/
  6. Build + test (R8)
  7. Commit com mensagem Conventional Commits
  8. Push via R9 (autorizacao)

## 4. PROTOCOLO DE FIM DE SESSAO

Toda sessao com 3+ commits OU 30+ minutos:

Passo 1: validacao final
  git status --short
  git log master..HEAD --oneline
  git diff HEAD --stat
  dotnet build
  
Passo 2: criar handoff em 
  docs/dev/sessoes/YYYY-MM-DD-HHMM-tema.md
  
Template do handoff:

  # Sessao <tema>
  
  Data: YYYY-MM-DD HH:MM
  Worktree: <path ou "master direto">
  Identidade Git: <email autoral / handle gh>
  Status final: completo | parcial | pausado | abandonado
  
  ## O que foi feito
  - ...
  
  ## O que ficou pendente
  - ...
  
  ## Decisoes tomadas
  - ...
  
  ## Commits criados
  - SHA1: <mensagem>
  
  ## Branches criadas/deletadas
  - ...
  
  ## Proxima acao recomendada
  - ...
  
  ## Referencias
  - Plano: docs/plan/<arquivo>
  - ADRs: ...
  - Incidentes: ...

Passo 3: cleanup
  - Worktrees nao-mais-usados: git worktree remove
  - Branches mergeadas: git branch -d
  - Stashes nao-relevantes: revisar e drop com autorizacao R9

## 5. ESTADO CONHECIDO DO REPO (snapshot 2026-05-16)

Master: sincronizado com origin (0/0 ahead/behind)
HEAD master: 01629922 chore(cleanup): higiene pos-incidente Fase 3
Build: verde, 30 warnings pre-existentes
Working tree: limpo

Worktrees ativos (6):
- master principal
- wt-* dedicados (calculadora-producao, claude-extractor-refactor,
  financeiro-lancamento, render-pwa-autoupdate, security-rls-postgres)

Pendencias documentadas para sessoes futuras:
- 27 PRs abertas (14 antigas, fazer triagem)
- 10 branches dev/* com trabalho ahead (revisao individual)
- 7 stashes antigos (revisao individual)
- 2 dirs fisicos travados em .claude/worktrees/ (process lock)
- Warning CS8602 SyncController.cs:903 (pequeno)

Decisao Nfe* vs NotaFiscal* RESOLVIDA: ADR-0018 (Aceito, 2026-05-17).
Master mantem Nfe* em codigo + "Nota Fiscal" em UI + "notas-fiscais" em REST.
PR #99 fechado como superseded (ver ADR-0018).

Plano avanco NF: docs/plan/nota-fiscal/00-README.md

Branch backup de seguranca: backup/master-pre-fase1-2026-05-16

## 6. ROADMAP PROXIMOS PASSOS

Etapa 1: Marco zero (deploy + tag v1.0)
Etapa 2: Defesas estruturais (branch protection + Husky + CI billing)
Etapa 3: Triagem 27 PRs abertas
Etapa 4: ROADMAP.md publicado
Etapa 5: Modulo novo (Caixa Conciliado V2 OU Rotulagem P-02)

Decisao Caixa vs Rotulagem: pendente, depende de validacao premissas.

## 7. RECURSOS DE LEITURA

Antes de operar:
- docs/dev/incidentes/2026-05-16-master-broken-wip-snapshot.md
- docs/dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md
- docs/adr/0011-nomenclatura-pt-br-rotulagem.md
- docs/adr/0013-cancellation-token-iusecase.md
- docs/dev/sessoes/2026-05-16-1245-fases-1-2-3-handoff-final.md
