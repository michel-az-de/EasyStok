# Baseline de medicao — Billing recorrente + MRR realizado (issue #754)

**Data:** 2026-06-30. **Estado:** ANTES das mudancas (fatia F0). **Refs:** #754, ADR-0037 (adendo), #274, #627.

Este documento congela o "antes" para comprovar melhoria no fim (par: `docs/billing-after.md`).
Cada secao marca `[VIAVEL NESTE PR]` vs `[ASPIRACIONAL / FORA DE ESCOPO]`, para nao prometer
o que o repo nao suporta hoje.

## Metodo (ferramentas reais do repo)

- **Cobertura** `[VIAVEL]`:
  `dotnet test EasyStok.CI.slnf -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings`
  -> `reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Cobertura`
  -> `./scripts/check-coverage-thresholds.ps1 -CoberturaXmlPath ./coverage-report/Cobertura.xml`.
- **Lint/format** `[VIAVEL]`: `dotnet format EasyStok.sln --verify-no-changes --include <arquivos-do-PR>`
  (NUNCA global — vermelho cronico em ~1900 arquivos pre-existentes).
- **Arquitetura** `[VIAVEL]`: `dotnet test --filter "Category=Architecture"` (gate R4).

## Gates de cobertura atuais (piso enforced)

Fonte: `scripts/check-coverage-thresholds.ps1` (rodado por `coverage.yml`, cron diario 06:00 UTC,
NAO gate sincrono de PR; o gate sincrono e `ci.yml` = build + test).

| Modulo | Gate (piso) |
|---|---|
| EasyStock.Domain | 70 |
| EasyStock.Application | 45 |
| EasyStock.Api | 9 |
| EasyStock.Infra.Async | 55 |

O PR **nao pode regredir** esses pisos.

**Gap medido:** o calculo de MRR vive hoje em `EasyStock.Infra.Postgre`
(`AdminDashboardQueries`, `AssinaturaEmpresaRepository`), que **nao tem gate de cobertura** e
cujos testes (`Infra.Postgre.IntegrationTests`) ficam **fora do `EasyStok.CI.slnf`**. Mover a
DEFINICAO de receita para uma camada coberta pelo CI e objetivo das fatias F1/F3.

## Cobertura ao vivo (ANTES) — PENDENTE de janela limpa

`[VIAVEL, mas bloqueado agora]` No momento desta fatia o working tree esta sob edicao
concorrente (WIP de Caixa em `EasyStock.Application`/`EasyStock.Infra.Postgre`) e ha ~37
processos `dotnet` segurando lock de `bin/obj`, o que torna a medicao de cobertura ao vivo
nao-confiavel. O percentual ao vivo de `EasyStock.Application` e `EasyStock.Api` (onde o
read-model e os jobs passam a viver) deve ser capturado numa janela limpa e congelado aqui
como piso antes do merge.

## Fora de escopo / aspiracional

- **Mutation testing** `[ASPIRACIONAL]`: Stryker.NET NAO esta no repo (sem `stryker-config.*`,
  sem `dotnet-stryker` em `.config/dotnet-tools.json`). Meta >=80% rebaixada -> issue futura
  agendada (workflow cron, meta inicial 60% no nucleo financeiro, subindo por onda como os
  gates de cobertura). Mutation roda a suite inteira por mutante: incompativel com o gate de
  PR de 20 min (`ci.yml`).
- **SonarQube, lizard** `[ASPIRACIONAL]`: ausentes. Complexidade ciclomatica pode sair do
  `Cobertura.xml` (ReportGenerator ja emite) se necessario, sem ferramenta extra.
- **OpenTelemetry tracing dos jobs** `[FORA DE ESCOPO]`: nao confirmado como instrumentado;
  a medicao realista do fluxo "ativar assinatura -> emitir fatura" e o logging estruturado
  (`ILogger` com `{FaturaId}`/`{Numero}`) no padrao dos jobs irmaos.

## Defesa anti-regressao do dinheiro neste PR

Sem Stryker, a garantia de qualidade-de-asserts vem dos **testes de invariante R8
deterministicos** (I1 calculo `Fatura.Total == PrecoMensal - desconto`; I2 cobertura
`assinaturas ativas sem fatura na competencia == vazio`; residuo explicado), rodados no CI.
Sao naturalmente mutation-resistentes.
