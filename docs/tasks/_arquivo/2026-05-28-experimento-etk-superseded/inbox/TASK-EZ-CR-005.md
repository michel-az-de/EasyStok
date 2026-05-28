# TASK-EZ-CR-005 — Decidir: deletar MetricsService ou instrumentar use cases

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-6)
**Prioridade:** P2
**Esforco:** P (Opcao A — deletar) ou M (Opcao B — instrumentar)
**Status:** inbox — **requer decisao do Felipe**

## Objetivo

Resolver pendencia do `MetricsService` que esta registrado em DI e injetado em 2 controllers, mas **nunca chamado em runtime** (validado por grep: 0 callers para qualquer metodo `Increment*`).

## Decisao necessaria

### Opcao A — Deletar (recomendada se nao houver demanda Prometheus/Grafana)

- Deletar `EasyStock.Api/Observability/MetricsService.cs`
- Remover registro em `EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs`
- Remover injecao em:
  - `EasyStock.Api/Controllers/ReportsController.cs`
  - `EasyStock.Api/Controllers/AdminReportsController.cs`
- Remover referencia em `EasyStock.Api/Configuration/ReportingApiExtensions.cs`

### Opcao B — Instrumentar use cases

- Adicionar chamadas `_metrics.IncrementEntradasEstoque()` em `RegistrarEntradaUseCase`
- Idem para Saidas, Reposicoes, Vendas
- Adicionar `_metrics.IncrementFalhasOperacao(operacao)` em catch global
- Definir whitelist de valores `operacao` (evitar cardinalidade explosiva via userId)
- Verificar se ha Prometheus/Grafana configurado em `Program.cs` (`AddOpenTelemetry`?)

## Validacao do achado

```powershell
git -C 'C:\easy\EasyStok' grep 'Increment(Entradas|Saidas|Reposicoes|Vendas|Falhas)Operacao' --include='*.cs'
# Resultado: 5 matches, TODOS em MetricsService.cs (definicoes)
# 0 callers em todo o projeto
```

## Definicao de Pronto

### Se Opcao A:
- [ ] Arquivo `MetricsService.cs` deletado
- [ ] DI registrations removidas
- [ ] Controllers nao injetam mais
- [ ] `dotnet build` verde
- [ ] Sem regressao em testes

### Se Opcao B:
- [ ] `IncrementEntradasEstoque` chamado em `RegistrarEntradaUseCase`
- [ ] Idem para outros Increment*
- [ ] Whitelist de `operacao` para `IncrementFalhasOperacao`
- [ ] Validar export para Prometheus (se aplicavel)
- [ ] Documentar em ADR

## Recomendacao

**Opcao A** se o projeto nao tem stack de observability ativa (Prometheus/Grafana). Codigo morto custa mais que reescrever quando precisar.

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-6)
