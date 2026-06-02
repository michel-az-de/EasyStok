# ADR-0023 — Estratégia e Padrões de Teste

**Status:** Aceito
**Data:** 2026-06-02
**Relacionados:** ADR-0001 (MongoDB descartado), ADR-0022 (master-first); issue #394 (umbrella)

## Contexto

Mapeamento de 2026-06-02: **1.879 métodos de teste** em 11 projetos (xUnit 2.9 +
NSubstitute + FluentAssertions + NetArchTest + Testcontainers + Xunit.SkippableFact).
Build verde. A auditoria expôs **verdes-falsos** — testes que passam sem verificar
nada, dando falsa confiança:

1. **~68 no-op skips** (`if (!fixture.IsAvailable) return;`) em
   `Infra.MongoDb.IntegrationTests` (25) e `Api.IntegrationTests` (~43): o `[Fact]`
   PASSA sem nenhuma asserção quando o Docker está fora. `Infra.Postgre.IntegrationTests`
   já fazia o certo — `[SkippableFact]` + `Skip.If`, que reporta **SKIPPED** em vez de PASSED.
2. **Shadow-mock**: `AsyncInfrastructureTests` redefinia `InMemoryCacheService`
   (classe real de `Infra.Async`), exercitando a reimplementação, não o código real.
3. **Gap de CI**: o PR gate (`ci.yml` → `EasyStok.CI.slnf`) roda **4 de 11** projetos;
   os demais só em `coverage.yml` no push. `dotnet test <sln> --no-build` sai 0 **sem
   rodar nada** se um projeto deixa de compilar/descobrir testes (verde-falso de harness).
4. Os 25 testes Mongo exercitavam **dead-code** (Mongo descartado por ADR-0001, lança
   `NotSupportedException` em prod) — verdes-falsos sobre código que nem pode ser selecionado.

## Decisão

1. **Test pyramid + onde cada camada roda:**
   - **Unit** (rápido, sem Docker): SEMPRE no PR gate (`ci.yml`).
   - **Integration** (Testcontainers): job de CI dedicado; `Skip.If` quando Docker ausente.
   - **Architecture + meta-higiene**: pre-commit (Husky) + PR gate, via `[Trait("Category","Architecture")]`.

2. **Padrões banidos** (falham o build via `EasyStock.ArchitectureTests/TestHygieneTests.cs`):
   - no-op skip por infra ausente (`if (!...Available) return;`);
   - `[Fact]`/`[Theory]` sem nenhuma asserção (allowlist explícita e justificada apenas
     para casos que delegam asserção a helper);
   - classe declarada em projeto de teste com nome simples igual a um tipo de produção (shadow-mock).

3. **`Skip.If` é o único jeito de pular por infra ausente:** `[SkippableFact]` /
   `[SkippableTheory]` + `Skip.If(!disponivel, motivo)`. Pular reporta **SKIPPED**,
   nunca PASSED. Pacote `Xunit.SkippableFact`.

4. **Política CI/coverage em ondas:** PR gate cobre unit + arch + meta (rápido e
   honesto); job de integração com Testcontainers (não-obrigatório até estabilizar,
   depois obrigatório); `coverage.yml` como rede profunda, protegido por **count-floor**
   (executados+skipped ≥ piso conhecido) contra o verde-falso do `--no-build`. Gates por
   módulo são evolutivos (Domain 70 / App 45 / Api 9↑ / Async 55).

5. **Enforcement por meta-teste com staging `ArchitectureDebt`→`Architecture`** (mesmo
   padrão de ControllerDiscipline, #337/#349): um detector novo entra como
   `Category=ArchitectureDebt` (lista sem bloquear commit) e vira `Category=Architecture`
   (gate) quando a lista zera. **Todo detector exige prova red-bar** — injetar uma
   violação, confirmar que o teste FALHA, reverter. Um gate que nunca fica vermelho é,
   ele mesmo, um verde-falso.

6. **Dead-code não tem teste vivo:** código descartado por ADR (ex.: Mongo/ADR-0001) é
   **removido fisicamente** junto com seus testes, não mantido com testes que só passam por skip.

## Consequências

### Positivas
- Verdes-falsos por skip silencioso, ausência de asserção e shadow-mock ficam
  **impossíveis por construção** (o meta-teste falha o build se reaparecerem).
- A suíte de integração reporta honestamente SKIPPED vs PASSED vs FAILED.
- PR gate cobre o que importa; harness à prova de `--no-build` vazio.

### Negativas / aceitas
- Ligar o job de integração de verdade **expõe falhas latentes** antes mascaradas
  (ex.: as 23/25 falhas Mongo da #201) — desejado, tratado como triagem, não como regressão.
- O detector assertion-free é **heurístico textual** (asserção em helper = falso-positivo)
  — mitigado por allowlist explícita + staging em `ArchitectureDebt` antes de virar gate.

## Referências
- Issue #394 (umbrella), #201 (Api+Mongo triagem), #390 (PWA honest runner),
  #337/#349 (ratchet de arch-test), #274 (cobertura controllers/use-cases).
- ADR-0001 (MongoDB descartado), ADR-0022 (master-first trunk-based).
- Modelo `Skip.If`: `EasyStock.Infra.Postgre.IntegrationTests`.
- Meta-teste (a implementar): `EasyStock.ArchitectureTests/TestHygieneTests.cs`.
