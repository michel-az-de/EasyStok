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

## EasyStock.ArchitectureTests / Exceptions_De_Domain_Devem_Ficar_No_Domain

- **Por que falha:** o teste valida que todas as classes terminando em `Exception` residem em `EasyStock.Domain.Exceptions`. Atualmente alguma exception fora do Domain está violando essa regra arquitetural — vista falha em `ArchitectureTests.cs:92` desde o commit `4b018b39 chore(p-02-f0.5)`.
- **Como confirmar:** `dotnet test EasyStock.ArchitectureTests --filter "FullyQualifiedName~Exceptions_De_Domain"` retorna `1 failed`. Falha determinística (não-flaky no sentido temporal — é regressão arquitetural não-corrigida).
- **Por que não foi corrigido:** mover/renomear a exception ofensora exige análise — pode ser intencional (ex: `GatewayFiscalException` em `EasyStock.Infra.Integrations`) ou regressão. Decisão precisa Felipe + revisão.
- **Quando promover a "corrigir":** quando Felipe priorizar limpeza arquitetural OU quando outra refatoração tocar Exceptions. Adicionar `[Trait("Category", "Architecture")]` ao teste depois para entrar no escopo do pre-commit Husky.
- **Nota:** este teste NÃO tem `[Trait("Category", "Architecture")]` ainda, então não bloqueia commits via Husky pre-commit (que filtra Category=Architecture). Apenas falha em runs explícitos.

---

## Política geral

- **Não marcar teste como flaky sem documentar aqui.** Sem entrada neste arquivo + comment no próprio teste, o "sabe-se que é flaky" não é compartilhável (e some quando a memória do dev some).
- **`[Trait("Category","Flaky")]`** não é usado intencionalmente — flaky deve ser visível em qualquer run, não escondido em categoria opcional.
- **Re-run automático em CI:** quando GitHub Actions voltar (billing bloqueado em 2026-05-11), considerar `--retry 2` apenas para testes desta lista.
