# Relatório de frugalidade de tokens — poka-yoke (ADR-0029)

Objetivo: medir o custo (tool-calls + re-derivação de contexto) de tarefas-tipo **antes** e **depois** dos guard rails. Ref: épico #530.

**Métrica:** `tool-calls` por tarefa (proxy concreto e contável de tokens) + o custo cognitivo de redescobrir/interpretar saídas. O "antes" vem das danças documentadas na memória **e desta própria sessão** (as Fatias 1-2 foram feitas com a dança MANUAL; as 3-7 com os comandos canônicos — um A/B direto, não hipotético). O "depois" são os fluxos de 1 comando, provados.

## T1 — Corrigir + deployar 1 nit no Web
**Antes** (~10-15 tool-calls + risco de retrabalho de classe inteira):
- Descobrir que `EasyStock.Web/wwwroot/etiqueta` é cópia (ler memória/grep) — e **risco de editar a cópia errada** → o build reverte → retrabalho (#527 perdeu 4 de 6 arquivos).
- Build da sln cheia falha por lock/maui → decifrar se é real → build em temp (~2-3 calls).
- Dança de commit (~4-6 calls, ver T2).
- Deploy: lembrar do manual + verificar sem HTTP (~3-4 calls).

**Depois:**
- Editar a cópia errada virou **impossível**: o hook PreToolUse nega na hora e aponta a fonte (0 retrabalho).
- `build-check`: 1 comando que funciona (lock-immune).
- `commit-seguro`: 1 comando.
- `deploy-verify`: 1 comando.

**Redução:** de ~10-15 calls (+ risco de retrabalho) para ~3-4 calls, **e a classe "editei a cópia gerada" deixou de existir**.

## T2 — Commit seguro em master (A/B MEDIDO nesta sessão)
**Antes** (dança manual, como fiz nas Fatias 1-2): ~4-6 tool-calls — HEAD-antes (1), status/identidade (1), stage+diff (1), commit por pathspec (1), validar HEAD autor/arquivos/mensagem (1), + recovery se o #448 sequestrar (1+). Cada passo exige o agente lembrar e interpretar a saída.

**Depois** (`commit-seguro`, Fatias 3-7): **1 tool-call**. O script faz identidade + stage por pathspec + diff + padrões proibidos + commit + validação de HEAD, e imprime o veredito (OK/ERRO). Mais o hook PostToolUse que valida o HEAD automaticamente após qualquer commit.

**Redução:** ~4-6 calls → **1** (medido: as 5 fatias commitadas por `commit-seguro` saíram em 1 call cada, com a validação de HEAD inclusa no resultado).

## T3 — Verificar que o deploy subiu na VM
**Antes** (~3-5 tool-calls, redescobertos toda sessão): lembrar que HTTP não funciona (TLS bare-IP), recordar a receita `az vm run-command`, escrever o `.sh` temp (gotcha LF), invoke, parsear GIT_SHA, comparar com HEAD.

**Depois:** `deploy-verify` **1 comando** (faz tudo + compara + veredito). Provado ao vivo: 1 call, container `GIT_SHA == HEAD`, exit 0 (VERDE).

**Redução:** ~3-5 → **1**.

## Ruído de CI (transversal a toda sessão)
**Antes:** todo commit/PR mostrava 3 checks vermelhos crônicos (coverage / deploy-render / dual-frontend-drift); distinguir real-vs-ruído exigia `gh run list` por workflow (~3+ calls + carga cognitiva "esse vermelho é meu?").

**Depois:** 0 vermelhos de ruído; os checks verdes significam o que dizem. Economia recorrente.

## Síntese

| Tarefa-tipo | Antes (tool-calls) | Depois | Como |
|-------------|--------------------|--------|------|
| T1 nit + deploy | ~10-15 + retrabalho | ~3-4, retrabalho 0 | hook PreToolUse + build-check + commit-seguro + deploy-verify |
| T2 commit seguro | ~4-6 | **1** | commit-seguro + hook PostToolUse |
| T3 verificar deploy | ~3-5 | **1** | deploy-verify |
| Decifrar CI | ~3+ por commit | **0** | quarentena (red = real) |

Além dos tool-calls: o `.poka-yoke/registry.yaml` + a memória atualizada cortam a **re-derivação** dos gotchas (a sessão lê o SoT em vez de re-investigar). É o "caro uma vez, barato sempre": o custo de construir a camada (esta sessão) amortiza em toda sessão futura.

**Caveat honesto:** os números "antes" são contagens de tool-calls das danças documentadas/observadas, não medição token-a-token de sessões históricas (que não tenho). `tool-calls` é um proxy fiel e contável; a redução é **estrutural** (1 comando vs. sequência manual) e inclui a **eliminação de classes inteiras de retrabalho** (editar cópia gerada, commit sequestrado, build vermelho-por-lock, vermelho-de-CI-que-não-é-meu).

## Status
Relatório fechado (criterio de métricas do épico #530). Para medição token-a-token futura: instrumentar uma sessão real rodando T1-T3 com os comandos canônicos e comparar com uma baseline manual.
