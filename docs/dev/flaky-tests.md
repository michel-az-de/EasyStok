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

## ✅ CORRIGIDO (#910) — timeout 500ms do ScribanRenderer no CI

Afetava dois testes, mesma causa: `ScribanRendererTests.Templates_diferentes_geram_resultados_independentes`
e `EmailTemplateRenderSmokeTests.Template_de_email_renderiza_sem_erro_de_sintaxe`.

- **Por que era flaky:** os testes exercitam o `ScribanRenderer`, que impunha `RenderTimeout = 500ms`
  hardcoded. Em runner do CI (primeiro run pós-JIT, GC, testes em paralelo) a primeira renderização
  passava de 500ms e disparava `TimeoutException : Renderizacao de template excedeu 500ms`. Visto no
  run 28685566400 (triagem #822) e depois **2/2 consecutivos, sem carga concorrente**, no run
  29111058019 (master pós-#909) — o gatilho ">5%/sprint" deste próprio doc, cruzado.
- **Fix (#910):** `ScribanRenderer.RenderTimeout` passou a ler `EASYSTOK_SCRIBAN_TIMEOUT_MS` (fallback
  500ms); `ci.yml` seta `2000` no step de teste. **Prod e dev local seguem em 500ms** — a guarda de
  sanidade não foi diluída, só o piso do runner lento do CI.
- **Se voltar a falhar mesmo com 2s:** aí é renderização de fato lenta (regressão), não timing —
  investigar o template, não subir mais o teto.

---

## Política geral

- **Não marcar teste como flaky sem documentar aqui.** Sem entrada neste arquivo + comment no próprio teste, o "sabe-se que é flaky" não é compartilhável (e some quando a memória do dev some).
- **`[Trait("Category","Flaky")]`** não é usado intencionalmente — flaky deve ser visível em qualquer run, não escondido em categoria opcional.
- **Re-run automático em CI:** quando GitHub Actions voltar (billing bloqueado em 2026-05-11), considerar `--retry 2` apenas para testes desta lista.
