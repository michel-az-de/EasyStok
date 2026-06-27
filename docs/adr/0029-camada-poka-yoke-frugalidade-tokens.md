# ADR-0029 — Camada poka-yoke + frugalidade de tokens (guard rails machine-enforceable)

**Status:** Aceito
**Data:** 2026-06-08
**Relacionados:** #530 (épico), #531 (Fatia 1), ADR-0022 (master-first), ADR-0023 (estratégia de testes), ADR-0024 (higiene de migrations — precedente do padrão arch-test anti-regressão), ADR-0025 (estratégia de teste frontend — enquadra a Fatia 5)

## Contexto

A Fase 0 (auditoria com evidência, 2026-06-08: forense de 500 commits em master, leitura de `.husky`/CI/`.csproj`, e `gh run list`) expôs dois custos recorrentes que documentação sozinha não resolve.

**1. Reincidência de classes inteiras de erro.** Medido:
- Dropdown Alpine regrediu 5x (#103, #479, shas `531fcaa9`/`e79a0576`/`8c4d4a60`): re-adicionar `.app-dropdown { display:none }` persistente em vez de `[x-cloak]`.
- `<form>` (FormTagHelper) descarta `@submit.prevent` no render (#481): modais submetiam nativo.
- `x-model` não captura preenchimento programático/autofill (#497, `941d7821`).
- Arquivo gerado editado na cópia (etiqueta Web copiada da Api, #527: commit saiu com 4 de 6 arquivos).
- Fora da taxonomia original, mas de alta frequência: seed/bootstrap ~9 re-fixes ("solução definitiva"), i18n/cultura ~12-15, Dockerfile sem COPY 3x.

**2. Custo de tokens por sessão.** Todo agente remede os mesmos gotchas, decifra CI cronicamente vermelho e refaz manualmente a dança de commit seguro. Cada verificação manual repetida é candidata a virar hook/skill/teste ("caro uma vez, barato sempre").

**Estado dos sinais (medido):**
- Verdes reais (gates): `ci.yml` (build `EasyStok.CI.slnf` + test), `secret-scan.yml`, `deploy-fly.yml`.
- Vermelhos crônicos (ruído): `coverage.yml`, `deploy-render.yml` (Render morto), `dual-frontend-drift.yml` (mirror Mobile dessincronizado desde 2026-06-03).
- O build mandado pelo §0 do CLAUDE.md (`dotnet build EasyStok.sln`) falha por **lock de bin** do ambiente local automático (#448): `MSB3021` em `EasyStock.Admin.exe`/`EasyStock.Web.exe`. Não é maui ausente — o Mobile compilou.

**Metade da taxonomia já tem defesa.** Classe E (Alpine) está corrigida no código com comentários-aviso (`app.css:165`, `_Layout.cshtml:46-50`, `form-modal.js:121-124` e `207-213`), mas **sem teste anti-regressão**. Classe G está totalmente coberta (ADR-0024: `MigrationDesignerHygieneTests` no gate + reconciliação idempotente). O gap não é a correção: é a proteção contra recorrência e o custo de redescoberta.

## Decisão

Adotar uma hierarquia de defesa e materializá-la em guard rails machine-enforceable, calibrados para não incentivar bypass.

**Hierarquia (do melhor pro pior):**
1. Tornar o erro impossível (o caminho errado não existe).
2. Detectar no ato (hook que bloqueia).
3. Detectar cedo (teste no gate / CI).
4. Documentar (último recurso — depende de alguém lembrar de ler).

**Pilares:**

1. **Manifesto vivo `.poka-yoke/registry.yaml`** — fonte única legível por máquina: arquivos gerados → fonte, comandos canônicos nomeados, e cada armadilha com seu detector e `status` (`active` | `planned`). Catalogado na Fase 0, ampliável com evidência medida (PS1).

2. **Hooks Claude Code (`.claude/settings.json`):**
   - `PreToolUse`: bloqueia (deny) `Edit`/`Write` em caminho gerado, apontando a fonte (Classe A).
   - `PostToolUse`: após qualquer `git commit`, valida HEAD (autor canônico, arquivos do escopo, mensagem com `closes #N`) e avisa se o auto-commit #448 sequestrou (Classe B).

3. **Comandos canônicos nomeados:** `build-check` (slnf + lock-immune), `arch-gate`, `commit-seguro`, `deploy-verify`. Qualquer agente invoca sem redescobrir.

4. **Gates confiáveis:** vermelho crônico vira verde ou quarentena (red = real). `.gitattributes linguist-generated`, headers `GENERATED` nas cópias, e testes anti-regressão na suíte `Category=Architecture` (mesmo padrão de reflexão da ADR-0024). Para Classe E, dentro do arcabouço da ADR-0025 (meta-lint / detector estilo `ArchitectureDebt`; **não** browser/E2E obrigatório).

5. **Calibragem de severidade:** `deny` só em sempre-erro (editar cópia gerada, commit com autor errado); `warn` no contextual (espelho de constante BFF). Meta dura: nenhum guard rail que o agente contorne com `--no-verify`. Um guard rail ignorado dá falsa segurança e é pior que nenhum.

**Métricas:** baseline de tool-calls/tokens para 3 tarefas-tipo (`docs/dev/poka-yoke/baseline-tokens.md`), instrumentação, e relatório de redução no fechamento de #530.

## Escopo e faseamento

Aprovado por Felipe (2026-06-08): **Classes A-D + skills (`commit-seguro`, `deploy-verify`) + manifesto**. Classes de maior frequência fora da taxonomia original (seed/bootstrap, i18n/cultura, Dockerfile COPY) ficam como **backlog rastreado**, não neste ADR.

Fatias (cada uma commit build-verde em master, fecha com `closes #N`), rastreadas em #530:
1. ADR-0029 + manifesto + baseline (esta).
2. Classe D: `build-check` lock-immune + troca do §0 do CLAUDE.md + quarentena dos 3 workflows vermelhos.
3. Classe B: `commit-seguro` + hook `PostToolUse`.
4. Classe A: hook `PreToolUse` + headers GENERATED + `.gitattributes` + verdejar `dual-frontend-drift`.
5. Classe E: testes anti-regressão Alpine (`Category=Architecture`, dentro da ADR-0025).
6. Classe F: skill `deploy-verify`.
7. Classe C: fluxo seguro de worktree + limpeza dos worktrees órfãos.

## Alternativas consideradas

- **Só documentar (CLAUDE.md + memória):** rejeitado como defesa primária — é o nível 4 da hierarquia; depende de alguém lembrar de ler. Documentação acompanha, não substitui o detector.
- **Hooks que bloqueiam tudo:** rejeitado — atrito vira bypass (`--no-verify`). Daí a calibragem `deny`/`warn`.
- **Browser/E2E (Playwright) para Classe E:** adiado, conforme ADR-0025 (onda C, não obrigatória até estabilizar). Fatia 5 usa teste estático/render, não E2E.

## Consequências

### Positivas
- Classes A-D ganham defesa machine-enforceable; Classe E deixa de poder regredir.
- Sinal de CI volta a significar problema real; queda de tokens por sessão (medida).
- Manifesto torna o estado do repo legível por máquina (objetivo de fluxo multi-agente).

### Negativas / aceitas
- Custo inicial de implementação (caro uma vez, barato sempre).
- Hooks Claude Code são por-`settings.json`: não impedem um humano fora do Claude Code de editar a cópia gerada. Daí a defesa em profundidade (hook + header GENERATED + teste de CI cópia==fonte).
- Classes H/I/L de alta frequência ficam para depois (backlog).

## Atualizações

**2026-06-27 (#705) — paridade de higiene de design no Admin.** A Fatia 5 (Classe E) e os guards de higiene de CSS/views nasceram cobrindo só o `EasyStock.Web` (os arch-tests liam paths hardcoded do Web; o próprio comentário do `CssHexHygieneTests` previa "o .css do Admin pode entrar numa extensão futura"). O `EasyStock.Admin` — cujo "Deck de Operações" é o trabalho visual mais forte do repo — só era coberto pelo `TokensCssDriftTests`. Estendido ao Admin via 3 arch-tests-espelho aditivos (`Category=Architecture`, mesmo gate), com allowlists medidas por forense adversarial:

- `AdminCssHexHygieneTests` — hex hardcoded em `EasyStock.Admin/wwwroot/css` (espelho do `CssHexHygieneTests`).
- `AdminRazorViewColorUtilityHygieneTests` — cor semântica crua do Tailwind em `EasyStock.Admin/Pages` (espelho do guard de views do Web).
- `AdminAlpineHygieneTests` — 4 traps de runtime que **já derrubaram o Admin** e não tinham guard estático: ordem de script (#469), double-init (BUG-003/#463), `x-for :key` nulo (BUG-011/#463), SRI/CDN bloqueado (incidente 2026-06-02). Registrados no manifesto como `admin-alpine-*` (Classe E). O trap `init(param)` não existe no Admin (varredura vazia → sem guard).

Continua dentro do arcabouço da ADR-0025 (meta-lint estático, sem browser/E2E). Não altera a decisão original; só amplia a cobertura do Pilar 1 (manifesto) e do Pilar 4 (gates) ao segundo frontend.

## Referências
- #530 (épico), #531 (Fatia 1), #705 (paridade Admin).
- Fase 0 (2026-06-08): forense de git, auditoria de gates, leitura de `.csproj`/`.husky`/CI.
- ADR-0022 (master-first), ADR-0023 (testes), ADR-0024 (padrão arch-test anti-regressão), ADR-0025 (frontend Alpine).
- Memória do agente: `local-env-autocommit-sweeps-staged-files`, `web-etiqueta-assets-build-copied-from-api`, `web-topbar-dropdown-xshow-cloak-gotcha`, `ci-coverage-render-red-maui`.
