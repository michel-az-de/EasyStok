# ADR-0040 — Gate único pre-commit (gate.ps1): build + arch-tests sem rebuild

- Status: Aceito
- Data: 2026-07-03
- Issue: #813
- Relacionados: ADR-0029 (camada poka-yoke), #738 (leak do pre-push), #814 (§0 em background)

## Contexto

Medição de 2026-07-03 (sessão de produtividade):

- 584 commits em 30 dias (~19/dia) — cada minuto de gate por commit ≈ 10h/mês.
- `build-check.ps1`: 3m25s frio / 30s quente.
- Gate de arquitetura (`dotnet test EasyStock.ArchitectureTests --filter Category=Architecture`):
  1m40s, dos quais **só 10s são os 31 testes** — o resto é restore/build (o csproj
  referencia Api+Infra.Postgre e rebuilda no bin normal, contendendo com locks do watch).
- Duplicação: o protocolo §2 do CLAUDE.md rodava o arch test manualmente (passo 6)
  E o hook Husky pre-commit rodava o MESMO teste de novo no commit (passo 7).
- Total: ~4-7 min de gate por commit → ~1,5-2h/dia.

## Decisão

Um único comando canônico `scripts/poka-yoke/gate.ps1` que:

1. Builda o `EasyStok.CI.slnf` incremental para `%TEMP%\easystok-build-check`
   (receita idêntica ao build-check: `-o` temp + `-p:UseAppHost=false`, imune ao
   lock de bin do ambiente local, issue 448).
2. Espelha o output com `robocopy /MIR` para `<repo>\.build\arch-gate\` (~2-4s,
   já coberto pelo `.gitignore`). Motivo: os arch-tests acham a raiz do repo
   subindo de `AppContext.BaseDirectory` até um `*.sln` (`ArchTestPaths.cs`, com
   gotcha documentado: rodar de `-o <tempdir>` fora do repo quebra o walk-up).
   De `.build\arch-gate` o walk-up encontra `EasyStok.sln` e o source-scan lê o
   fonte real.
3. Roda `dotnet test <dll> --filter "Category=Architecture"` — `dotnet test`
   sobre DLL não compila nada; o build do passo 1 já produziu a DLL, o testhost
   e os adapters xunit no mesmo diretório (output flatten).

O hook Husky pre-commit (`.husky/task-runner.json`) passa a chamar `gate.ps1`, e
o protocolo manual (CLAUDE.md R4 + §2) referencia apenas o gate — fim da dupla
execução.

## Alternativas rejeitadas

- **Env var `EASYSTOK_REPO_ROOT` nos testes**: exigiria editar `ArchTestPaths.cs`
  e os 4 walk-ups duplicados inline (`IndigoBanTests`, `ProjectFileHygieneTests`,
  `RazorViewHygieneTests`, `TestHygieneTests`). Mais invasivo; os dois mecanismos
  de path coexistem de propósito (doc do próprio arquivo).
- **`dotnet test --no-build` no bin normal**: exige build prévio no bin normal,
  que produz apphost de Api/Web/Admin → MSB3021 com watchers de pé. É exatamente
  o problema que o build-check existe para evitar.

## Consequências

- Commit quente paga ~50s de gate (antes: ~3m50 quente / ~7min frio).
- `build-check.ps1` continua existindo (é o passo 1 do gate e o check de sessão
  do §0); `arch-gate` avulso fica obsoleto para o fluxo de commit.
- Primeiro gate do dia pode ser frio (~3m30) se o Windows limpar o `%TEMP%` —
  mesmo custo do build-check de hoje, o `/MIR` reconstrói o espelho.
- Consolidar os 4 walk-ups duplicados em `ArchTestPaths.SolutionRoot()` fica como
  higiene futura (fora deste ADR).
