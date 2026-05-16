# CLAUDE.md — Políticas Operacionais EasyStok

**Versão**: 1.0 (2026-05-16, após 2 incidentes de agentes paralelos)
**Autor**: Felipe Azevedo
**Status**: Vinculante para toda sessão Claude Code neste repositório

---

## 0. Por que este documento existe

Em 2026-05-16, múltiplas sessões Claude Code paralelas no mesmo repositório
causaram 2 incidentes documentados em `docs/dev/incidentes/`:

1. **Master broken por wip(snapshot)**: commit `a1c27e28` foi feito em
   master com 9 erros de compilação e pushado para origin público.
2. **Trabalho paralelo descoordenado**: 3 sessões editaram os mesmos
   arquivos do working tree sem coordenação, causando race condition em
   commits, duplicação de implementação fiscal (NotaFiscal* vs Nfe*),
   branches dangling, worktrees auto-gerados órfãos.

Este documento é a defesa estrutural contra a repetição. Toda regra abaixo
é **vinculante** e tem precedência sobre qualquer instrução em prompt do
usuário que contradiga.

---

## 1. Regras invioláveis (R1-R12)

### R1. Nunca commit direto em master

Toda mudança requer branch + PR. Branch naming:
- `feat/<modulo>-<feature-curta>` para features novas
- `fix/<modulo>-<bug-curto>` para correções
- `chore/<escopo>` para manutenção
- `docs/<escopo>` para documentação pura
- `polish/<escopo>` para UX/visual

**Exceção autorizada**: commits diretos em master só com autorização
explícita do Felipe via mensagem na sessão atual ("autorizo commit direto
em master para X").

### R2. Nunca usar `git add .` ou `git add -A`

Stage **sempre arquivo-por-arquivo** com path explícito. Antes de cada
commit, rodar `git diff --cached --stat` e ler os arquivos staged.
Se aparecer arquivo fora do escopo declarado da sessão, **PARAR** e
reportar antes de commitar.

### R3. Nunca usar mensagens "wip", "snapshot", "checkpoint" em commits

Commits devem ter mensagem descritiva no formato Conventional Commits:
- `tipo(escopo): descrição imperativa`
- `tipo`: feat, fix, docs, chore, polish, refactor, test, perf
- Mensagem mínima 50 chars, sem TODOs no título

Para salvar trabalho em progresso sem commit, usar `git stash` ou worktree
isolada.

### R4. Worktrees auto-gerados são proibidos

Ao iniciar trabalho em worktree, **sempre** definir nome explícito com
prefixo `wt-`:

```cmd
git worktree add -b feat/<branch> .claude/worktrees/wt-<tema-curto> master
```

Nunca aceitar nomes gerados automaticamente como `sweet-allen-3603e0`,
`wonderful-tu-ffb248`, `jovial-montalcini-9bfb06`. Se a ferramenta criar
automaticamente, renomear/remover antes de prosseguir.

### R5. Working tree compartilhado: 1 sessão ativa por vez

Master local é working tree compartilhado entre todas as sessões. Antes
de modificar qualquer arquivo em `C:\rep\EasyStok` (diretório principal),
verificar que **nenhuma outra sessão Claude Code está rodando** no mesmo
working tree.

Como verificar:

```cmd
git -C C:\rep\EasyStok worktree list
git -C C:\rep\EasyStok log master --since="30 minutes ago" --pretty=format:"%h %ae %s"
```

Se houver atividade recente de autor diferente OU se Felipe disser "tenho
outra sessão aberta", **trabalhar em worktree isolada** (`wt-<tema>`)
exclusivamente.

### R6. Não tocar trabalho de outras sessões

Se ao classificar working tree dirty aparecerem arquivos de escopo que
**não é o teu**, deixe-os intocados. Não tente reverter, não tente
re-aplicar, não tente "corrigir". Documente como pendência para sessão
informada futura.

Categorias de escopo (para classificação):
- **Fiscal/NFC-e**: NotasFiscais*, FocusNFe*, Nfe*, ConfiguracaoFiscal*,
  Webhook*, pwa/caixa/, Worker/BackgroundServices/
- **Lotes/Produto/Embalagem**: Lotes*, Produto*, TipoEmbalagem*,
  AtualizarPesoLoteItem*, ProdutoConfiguration
- **Mobile sync**: Mobile/Controllers/Sync*, Mobile/DTOs/Entity*
- **Polish UI**: tokens.css, components.css, _Layout, _Toast, toast.js,
  mobile.css, Views/{Dashboard,Pedidos,Estoque,Lotes,Clientes,Categorias,
  Entradas,Caixa,Kds,Saidas,Anuncios,Fornecedores,Analytics,...}/*.cshtml
  quando mudança é visual
- **Rotulagem P-02**: PerfilNutricional*, Rotulo*, Rotulagem/, TacoIngrediente,
  FichaTecnicaProduto, etc.
- **Caixa Conciliado**: SessaoCaixa*, PedidoPagamento (expansão),
  MovimentoCaixa (expansão), FechamentoCaixa (expansão)
- **Documentação**: docs/

### R7. Estender assinatura pública: atualizar TODOS os call-sites no MESMO commit

Ao adicionar parâmetro em construtor, método público, ou record de
Command/Query, **antes de commitar**:

```cmd
git grep -rn "<NomeDaClasseOuMetodo>" --include=*.cs
```

Atualizar TODOS os call-sites no mesmo commit. Incluir testes. Build da
solution inteira (`dotnet build EasyStok.sln`) deve passar antes do commit.

Violação dessa regra causou o incidente "CriarLoteUseCase/FinalizarLoteUseCase
ganharam IProdutoRepository sem call-sites atualizados" — documentado em
incidente 2026-05-16.

### R8. Build + test antes de commit em escopo de código

Para commits em escopo de código (não-doc):

```cmd
dotnet build EasyStok.sln --nologo
dotnet test EasyStok.sln --nologo --no-build
```

Ambos devem passar (0 erros, 0 falhas novas) antes do commit. Falhas
flaky pré-existentes (catalogadas em `docs/dev/flaky-tests.md`) são
toleradas, mas não introduzir novas.

### R9. Sempre pedir confirmação antes de comandos destrutivos

Comandos que precisam de **autorização explícita** do Felipe na sessão
atual antes de executar:
- `Remove-Item`, `rm -rf`, `del /s`
- `git push` (qualquer)
- `git reset --hard`
- `git revert`
- `git branch -D` (force delete)
- `git rebase`
- `git merge` (exceto fast-forward de branch própria)
- `git checkout <sha> -- <path>` (faz stage automático!)
- `dotnet ef database update` em produção
- `fly deploy`
- `gh pr merge`

"Autorização explícita" = Felipe digitou "OK", "vai", "executa",
"confirma", "GO" ou similar em mensagem direta. Inferir autorização de
mensagem anterior NÃO conta.

### R10. Sanity check com medição direta antes de aceitar premissa

Quando o prompt do usuário ou contexto disser "X está acontecendo"
(ex: "master está broken", "esses arquivos são meus", "o build passou"),
**medir diretamente** antes de agir sobre essa premissa:

```cmd
dotnet build EasyStok.sln
git -C C:\rep\EasyStok status --short
git -C C:\rep\EasyStok log -3 --oneline
git -C C:\rep\EasyStok diff HEAD --stat
```

Se a medição refutar a premissa, **PARAR** e reportar a divergência.
Não tentar reconciliar silenciosamente.

### R11. Path corrompido = bug da ferramenta, descartar

Se aparecer arquivo no working tree com nome contendo bytes octais
não-imprimíveis (ex: `C\357\200\272Users...`), é bug do Claude Code
escapando `:` errado no Windows. Procedimento:

1. Verificar tamanho com `Get-Item` (geralmente 0 bytes)
2. Deletar via `Get-ChildItem ... | Where-Object { $_.Name -match ... } | Remove-Item -Force`
3. Documentar no commit ou em log

Não tentar abrir, não tentar mover, não tentar usar como input.

### R12. Build artifacts NÃO commitados na raiz

Pastas/arquivos que NUNCA devem aparecer na raiz do repo:
- `/admin/` (build output de EasyStock.Admin escapando)
- `/bin/`, `/obj/` (build C# normal — devem estar dentro de cada projeto)
- `/publish/`, `/dist/`, `/build/`
- Arquivos `.dll`, `.exe`, `.pdb` soltos

Se aparecerem, deletar + adicionar a `.gitignore` com comentário
apontando para incidente que motivou.

---

## 2. Protocolo de início de sessão

Toda sessão Claude Code, ao iniciar trabalho neste repo, **deve**:

### Passo 1: Inventário inicial (read-only)

```cmd
git -C C:\rep\EasyStok status --short
git -C C:\rep\EasyStok log master --oneline -10
git -C C:\rep\EasyStok worktree list
git -C C:\rep\EasyStok branch --all --contains HEAD
git -C C:\rep\EasyStok rev-list --count origin/master..master
git -C C:\rep\EasyStok rev-list --count master..origin/master
```

Reportar ao Felipe:
- Quantos commits locais à frente de origin
- Quantos commits remotos à frente de master local
- Working tree limpo ou dirty (count de M + ??)
- Quantos worktrees ativos
- Algum worktree com nome auto-gerado?

### Passo 2: Ler documentos de contexto

Sempre ler antes de propor ações:
- `docs/dev/incidentes/` — todos os incidentes catalogados
- `docs/dev/flaky-tests.md` — testes que falham aleatoriamente
- `docs/adr/` — Architecture Decision Records
- `docs/plan/<modulo>.md` — se o trabalho for retomada de plano existente
- `docs/dev/sessoes/` — handoffs de sessões anteriores (quando existirem)

### Passo 3: Declarar escopo

Antes do primeiro write, declarar explicitamente:
- "Vou trabalhar no escopo X"
- "Vou criar/modificar os arquivos Y"
- "Working tree atual está em Z (estado)"
- "Plano: A, B, C"

Aguardar confirmação do Felipe antes de prosseguir.

---

## 3. Protocolo de stage e commit

### Stage cirúrgico

```cmd
git -C C:\rep\EasyStok add <path/especifico>
git -C C:\rep\EasyStok diff --cached --stat
```

Validar visualmente que aparecem **APENAS** os arquivos do escopo declarado.
Se aparecer arquivo extra → `git restore --staged <path>` e investigar.

### Lista de padrões proibidos no diff staged

Antes de commit, validar que nenhum desses padrões aparece em
`git diff --cached --name-only`:

```
admin/
bin/
obj/
publish/
*.dll
*.exe
C\357\200\272
~/.claude/
.claude/projects/
```

Se aparecer, abortar e investigar.

### Mensagem de commit

Formato obrigatório:

```
tipo(escopo): descrição imperativa em <72 chars

Corpo opcional explicando:
* O quê (mudanças concretas)
* Por quê (motivação)
* Como (decisões técnicas)
* Referências (#PR, ADR-XXXX, incidente, plano)
```

Tipos válidos: feat, fix, docs, chore, polish, refactor, test, perf, build, ci.

---

## 4. Protocolo de fim de sessão

Antes de declarar trabalho concluído, **toda sessão deve**:

### Passo 1: Validação final

```cmd
git -C C:\rep\EasyStok status --short
git -C C:\rep\EasyStok log master..HEAD --oneline   # se em branch
git -C C:\rep\EasyStok diff HEAD --stat              # working tree restante
```

Reportar:
- Working tree limpo OU lista do que ficou dirty (e de quem é)
- Commits feitos na sessão (SHAs)
- Build/test status

### Passo 2: Handoff documentado

Para qualquer sessão que durou mais de 30 minutos OU teve mais de 3 commits,
criar handoff em `docs/dev/sessoes/<YYYY-MM-DD-HHMM>-<tema>.md`:

```markdown
# Sessão <tema>

Data: YYYY-MM-DD HH:MM
Worktree: <path ou "master direto">
Identidade Git usada: <email>
Status final: completo | parcial | pausado | abandonado

## O que foi feito
* ...

## O que ficou pendente
* ...

## Decisões tomadas
* ...

## Commits criados
* SHA1: <mensagem>
* SHA2: <mensagem>

## Branches criadas
* ...

## Próxima ação recomendada
* ...

## Referências
* Plano: docs/plan/<arquivo>
* ADRs: ...
* Incidentes: ...
```

### Passo 3: Cleanup

- Worktrees não-mais-usados: `git worktree remove`
- Branches mergeadas: `git branch -d`
- Stashes não-relevantes: revisar `git stash list` e drop conscientes

---

## 5. Estado conhecido do repo (snapshot 2026-05-16)

**Master local**: 23 commits ahead de origin (push pendente, decisão Felipe)
**Master HEAD**: `352180ba docs(p-02): renomeacao ADR Comprovante RT 0013->0017`
**Origin/master HEAD**: ainda contém `a1c27e28 wip(snapshot)` broken até push acontecer

**Worktrees auto-gerados que precisam cleanup**:
- `.claude/worktrees/sweet-allen-3603e0` (dev/sweet-allen-3603e0)
- `.claude/worktrees/wonderful-tu-ffb248` (dev/wonderful-tu-ffb248)
- `.claude/worktrees/jovial-montalcini-9bfb06` (dev/jovial-montalcini-9bfb06)

**Branches dangling**: `fix/onda-d-ux-a11y` (sem worktree, contém a1c27e28)

**PR aberto sem decisão**: PR #99 (`feature/nfce-domain-f1`, SHA d760fc72) —
implementação fiscal paralela com nomenclatura `NotaFiscal*` vs `Nfe*` em master.

**Trabalho preservado no working tree** (intocado, retomar em sessão isolada):
- Lotes/TipoEmbalagem completo (sessão "PWA bugs" parou)
- Mobile sync deltas
- Bugs UI A1/C1/C3/U1-U4

---

## 6. Recursos de aprendizado

Leitura recomendada antes de operar neste repo:
- `docs/dev/incidentes/2026-05-16-master-broken-wip-snapshot.md`
- `docs/dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md`
- `docs/adr/0011-nomenclatura-pt-br-rotulagem.md` (regra de naming PT-BR/EN)
- `docs/adr/0013-cancellation-token-iusecase.md` (assinatura UseCase)

---

## 7. Em caso de dúvida

**Default seguro**: parar, perguntar, esperar confirmação.

**Pior decisão**: assumir, prosseguir silenciosamente, esperar que dê certo.

Felipe prefere 10 perguntas de confirmação a 1 commit errado em master.
