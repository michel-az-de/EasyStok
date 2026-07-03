# Testes Flaky Conhecidos

Inventário de testes que falham de forma transiente (timing, ordem de execução, race condition aceito). Cada entrada precisa ter:
- **Por que é flaky** (causa raiz)
- **Como confirmar** (re-run, condição específica)
- **Por que não foi corrigido** (custo vs valor)
- **Quando promover a "corrigir"** (gatilho de cancelamento da tolerância)

Sem este inventário, próxima sessão tropeça no teste e perde 30 min investigando algo já conhecido.

---

## EasyStock.Application.Tests / PollingOutboxSignalerTests.WaitAsync_completa_quando_intervalo_passa

- **Por que é flaky:** o teste cria um `PollingOutboxSignaler(TimeSpan.FromMilliseconds(50))` e aguarda `Task.Delay(150)` antes de validar que `task.IsCompleted == true`. Em runner sob carga (CI lento, máquina dev compilando outra coisa), o scheduler do .NET pode atrasar o tick em ~100ms+ e a asserção falha.
- **Como confirmar:** re-run resolve. Falha aparece tipicamente no primeiro test run após cold-start ou após qualquer outro teste com alto uso de CPU. Estabilidade próxima de 99% em runs limpos.
- **Por que não foi corrigido:** corrigir exige (a) trocar `Task.Delay` por `FakeTimeProvider` injetável e refatorar `PollingOutboxSignaler` para aceitar `TimeProvider`, ou (b) aumentar a tolerância (`Task.Delay(500)`) e aceitar suite mais lenta. Ambos têm custo de refator/tempo de suite que não compensa para um teste que valida comportamento óbvio do `Task.Delay`.
- **Quando promover a "corrigir":** se a flakiness subir de 1 falha esporádica para >5%/sprint, OU se outro teste de Notifications passar a flakar em conjunto (sinal de problema sistêmico de timing).

---

## EasyStock.Api.UnitTests / ScribanRendererTests.Templates_diferentes_geram_resultados_independentes

- **Por que é flaky:** o teste impõe timeout de 500ms na renderização do template Scriban. Em runner do CI sob carga (primeiro run pós-JIT, GC, outros testes em paralelo), a primeira renderização pode passar de 500ms e o `TimeoutException : Renderizacao de template excedeu 500ms` dispara. Visto no run 28685566400 (triagem issue 822) junto com o PollingOutboxSignaler — mesmo padrão de timing em runner lento.
- **Como confirmar:** re-run resolve; passa de forma estável em máquina local.
- **Por que não foi corrigido:** o timeout é uma guarda de sanidade (template não pode custar segundos), não uma spec de latência; subir o limite dilui a guarda e injetar relógio custa refator que não compensa.
- **Quando promover a "corrigir":** se falhar >5%/sprint, subir o limite para 2s no ambiente de CI (via env var) mantendo 500ms local.

---

## Política geral

- **Não marcar teste como flaky sem documentar aqui.** Sem entrada neste arquivo + comment no próprio teste, o "sabe-se que é flaky" não é compartilhável (e some quando a memória do dev some).
- **`[Trait("Category","Flaky")]`** não é usado intencionalmente — flaky deve ser visível em qualquer run, não escondido em categoria opcional.
- **Re-run automático em CI:** quando GitHub Actions voltar (billing bloqueado em 2026-05-11), considerar `--retry 2` apenas para testes desta lista.
