# Baseline de frugalidade de tokens — poka-yoke (ADR-0029)

Objetivo: medir o custo (tool-calls + tokens) de tarefas-tipo **antes** dos guard rails e **depois**, para provar a redução (objetivo de primeira classe da missão). Ref: #530.

## Tarefas-tipo

### T1 — Corrigir + deployar 1 nit no Web
Fluxo atual (sem guard rails), pelas danças recorrentes registradas na memória:
- Redescobrir que `EasyStock.Web/wwwroot/etiqueta` é cópia da Api (ou editar a cópia errada e o build reverter silenciosamente — #527).
- Build da sln cheia falha por lock (#448) → decifrar se é erro real → buildar em pasta temp.
- Commit: arriscar o sweep do auto-commit #448 → validar HEAD → recovery se sequestrado.
- Deploy: lembrar do deploy manual da VM → verificar sem HTTP (TLS bare-IP).
- **Baseline (a instrumentar):** `N1` tool-calls, `T1` tokens.
- **Meta depois:** hook bloqueia a cópia (0 retrabalho), `build-check` 1 comando, `commit-seguro` 1 comando, `deploy-verify` 1 comando.

### T2 — Abrir issue + commit seguro em master
Atual: abrir issue (gh), stage por pathspec, `diff --cached`, build, commit, validar HEAD, recovery se #448 sequestrar (autor/mensagem errados, `closes #N` não dispara).
- **Baseline:** `N2` / `T2`.
- **Meta depois:** `commit-seguro` encapsula a dança; `PostToolUse` valida HEAD automaticamente.

### T3 — Verificar que o deploy subiu na VM
Atual: lembrar que HTTP não funciona, `az vm run-command`, `docker inspect` GIT_SHA, grep do asset servido, checar healthy — sequência manual redescoberta toda sessão.
- **Baseline:** `N3` / `T3`.
- **Meta depois:** `deploy-verify` 1 comando idempotente.

## Metodologia
- Baseline capturado de transcripts reais e da estimativa das danças documentadas na memória.
- Pós: re-rodar a tarefa-tipo com os comandos canônicos e contar tool-calls/tokens.
- Reportar redução absoluta e percentual por tarefa no fechamento de #530.

## Status
Fatia 1: metodologia e tarefas-tipo definidas. Os números de baseline (`N`/`T`) serão instrumentados nas Fatias 2-3, quando os comandos canônicos existirem e der para comparar A/B com medição real (não estimativa).
