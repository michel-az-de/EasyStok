# Testes Flaky Conhecidos

Inventário de testes que falham de forma transiente (timing, ordem de execução, race condition aceito). Cada entrada precisa ter:
- **Por que é flaky** (causa raiz)
- **Como confirmar** (re-run, condição específica)
- **Por que não foi corrigido** (custo vs valor)
- **Quando promover a "corrigir"** (gatilho de cancelamento da tolerância)

Sem este inventário, próxima sessão tropeça no teste e perde 30 min investigando algo já conhecido.

---

## ✅ CORRIGIDO (#1680) — teste do canário de telemetria dependia do timeout de produção

Visto em `TelemetriaGeocodingTests.Sem_a_protecao_o_canario_vaza_no_span_e_no_log` (os três testes da
classe tinham a mesma exposição).

- **Por que era flaky:** o teste (I8.2, #1661) usa os clientes reais de CEP e de geocodificação contra
  um servidor local, e eles vinham com o timeout de produção (1 s no ViaCEP, 2 s no Nominatim). No run
  38318901505 (master `8831b2a9`) a primeira chamada, fria, passou de 1 s; o cliente do ViaCEP devolveu
  nulo, como manda o contrato dele, e o teste caiu em `NullReferenceException` na linha 93, com duração
  de 1 s. Já tinha falhado uma vez na máquina de dev antes do merge e foi mal diagnosticado como
  contagem de spans.
- **Como foi confirmado:** com o servidor local respondendo em 1,5 s, os três testes falhavam em 3 de 3.
- **Fix (#1680):** `PrazoLargoSoNoTeste` sobrepõe o timeout dos `HttpClient` só dentro do teste, e o
  servidor local passou a demorar 1,2 s de propósito. Assim o teste é determinístico nos dois sentidos:
  quem tirar o prazo largo vê a falha sempre, não de vez em quando.
- **Se voltar a falhar:** não é mais prazo. Olhar o que foi para span ou log.

---
## ✅ CORRIGIDO (#1117) — gate de worktrees paralelos compartilhava a pasta de build

Visto em `MigrationDesignerHygieneTests` (qualquer arch-test pode ser a vitima).

- **Por que era flaky:** `gate.ps1` e `build-check.ps1` (ADR-0040/ADR-0029) compilavam com
  `-o %TEMP%\easystok-build-check`, pasta unica para todos os worktrees da maquina. Em 2026-09-29, com
  sessoes paralelas, o pre-commit de `chat-site-1097` falhou com `ReflectionTypeLoadException`
  (`ISessaoChatSiteRepository`/`SessaoChatSite` nao encontrados) e o stack trace apontava para
  `gate-zero-arch-tests`: o build do outro worktree sobrescreveu a saida entre o build e o robocopy.
  Reexecutar passava. Corrida, nao regressao.
- **Fix (#1117):** `scripts/poka-yoke/build-out-dir.ps1` deriva a pasta de um hash do caminho do repo:
  `%TEMP%\easystok-build-check-<hash8>`. Estavel no mesmo worktree (incremental preservado), distinta
  entre worktrees. As pastas antigas/orfas em `%TEMP%` podem ser apagadas a mao sem risco.
- **Se voltar a falhar:** confira se o stack trace aponta para outro caminho de repo; se sim, alguem
  reintroduziu pasta de saida fixa.

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

## ⚠️ AMBIENTE (#1110) — Smart App Control bloqueia a DLL dos arch-tests no gate

Não é teste flaky, é bloqueio da máquina local. Fica aqui porque aparece como "gate estranho" no pre-commit.

- **Sintoma:** em 2026-09-29 o Windows Smart App Control bloqueou
  `.build\arch-gate\EasyStock.ArchitectureTests.dll` ("Uma política de Controle de Aplicativo bloqueou
  este arquivo", `0x800711C7`). O vstest pulou o assembly, disse "Nenhum teste corresponde ao filtro
  `Category=Architecture`" e **saiu 0**. O `gate.ps1` confiava só no exit code e dava VERDE falso.
- **Fix (#1110):** o `gate.ps1` grava TRX em `.build\arch-gate-results\arch.trx` e exige
  `Counters/@executed > 0`. Zero testes executados agora é VERMELHO com **exit 4**.
- **Se o gate der exit 4:** confira a mensagem do vstest. Com `0x800711C7`, o bloqueio é do Smart App
  Control (Segurança do Windows > Controle de aplicativos e navegador); rode de novo ou valide pelo CI.
  **Não** contorne com `--no-verify` nem afrouxe a checagem do TRX.

---

## Política geral

- **Não marcar teste como flaky sem documentar aqui.** Sem entrada neste arquivo + comment no próprio teste, o "sabe-se que é flaky" não é compartilhável (e some quando a memória do dev some).
- **`[Trait("Category","Flaky")]`** não é usado intencionalmente — flaky deve ser visível em qualquer run, não escondido em categoria opcional.
- **Re-run automático em CI:** quando GitHub Actions voltar (billing bloqueado em 2026-05-11), considerar `--retry 2` apenas para testes desta lista.
