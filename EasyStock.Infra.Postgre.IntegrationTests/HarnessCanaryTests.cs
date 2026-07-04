using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests;

/// <summary>
/// Guarda anti-falso-verde do projeto (issue #823). Espelha o canario do
/// Inventario (<c>ProdutoRowLevelSecurityTests.Harness_executa_de_verdade_quando_env_var_presente</c>):
/// em CI a env var <c>EASYSTOCK_IT_PG</c> existe, entao este teste NAO pula. Se o
/// harness Testcontainers estiver indisponivel (Docker degradado no runner), os 86
/// <c>[SkippableFact]</c> deste projeto SKIPariam e o gate ficaria verde-vazio — os
/// 4 fluxos criticos aqui (caixa fechamento, estoque concorrencia, cache
/// invalidation, EfiPix-fatura, storefront xmin) sumiriam sem alarme. Aqui isso vira
/// VERMELHO. Localmente, sem a env var, SKIPa visivel (Skip.If, nao no-op silencioso;
/// ADR-0023). Ver #394 (F4/count-floor) e #823.
/// </summary>
public class HarnessCanaryTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public void Harness_executa_de_verdade_quando_env_var_presente()
    {
        var envPresente = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG"));
        Skip.If(!envPresente,
            "EASYSTOCK_IT_PG ausente (rodada local sem o service container do CI).");

        fixture.IsAvailable.Should().BeTrue(
            "EASYSTOCK_IT_PG setada (CI) mas o harness Postgres/Testcontainers esta "
            + "indisponivel — os 86 SkippableFact deste projeto pulariam verdes: "
            + (fixture.UnavailableReason ?? "(sem motivo)"));
    }
}
