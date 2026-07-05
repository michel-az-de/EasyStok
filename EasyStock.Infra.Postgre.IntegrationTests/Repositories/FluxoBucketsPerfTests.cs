using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Medição/regressão de performance de <see cref="FluxoCaixaQueries.FluxoBucketsAsync"/>.
/// O método antes emitia 4 SumAsync POR bucket (até 24 buckets = 96 SELECTs sequenciais);
/// após a otimização deve emitir um número FIXO de queries (agregação por bucket), independente
/// da quantidade de buckets. Este teste conta os comandos EF executados e trava o teto.
///
/// Autocontido: usa a connection string em EASYSTOCK_PERF_PG (Postgres real; ex.: instância
/// local em localhost:5432) e um banco probe dedicado. Skipa se a env var estiver ausente,
/// para não quebrar ambientes sem Postgres acessível ao dotnet (Docker no WSL não é visto
/// pelo Testcontainers no dotnet-Windows).
/// </summary>
public sealed class FluxoBucketsPerfTests(ITestOutputHelper output)
{
    private const string EnvVar = "EASYSTOCK_PERF_PG";

    [SkippableFact]
    public async Task FluxoBuckets_diario_24_buckets_usa_poucas_queries_e_bate_valores()
    {
        var baseConn = Environment.GetEnvironmentVariable(EnvVar);
        Skip.If(string.IsNullOrWhiteSpace(baseConn),
            $"Defina {EnvVar} com a connection string de um Postgres real para rodar a medição.");

        // Banco probe dedicado (não toca o banco de trabalho).
        var conn = new Npgsql.NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = "easystock_perf_probe"
        }.ConnectionString;

        // ── Setup: recria o schema limpo no banco probe ────────────────────────
        await using (var setup = NewContext(conn, out _))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();
        }

        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;

        // ── Seed: 1 conta a pagar (100 @ hoje+2) e 1 a receber (200 @ hoje+3), emitidas ──
        await using (var seed = NewContext(conn, out _))
        {
            seed.SetMobileTenantContext(empresaId);
            seed.Empresas.Add(new Empresa
            {
                Id = empresaId,
                Nome = "Perf Probe",
                Documento = empresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            var catDespesa = CategoriaFinanceira.Criar(empresaId, "Despesas", TipoCategoriaFinanceira.Despesa);
            var catReceita = CategoriaFinanceira.Criar(empresaId, "Receitas", TipoCategoriaFinanceira.Receita);
            seed.CategoriasFinanceiras.Add(catDespesa);
            seed.CategoriasFinanceiras.Add(catReceita);

            var cp = ContaPagar.Criar(empresaId, null, catDespesa.Id, "CP Perf", hoje);
            cp.AdicionarParcela(1, 100m, hoje.AddDays(2));
            cp.Emitir();
            seed.ContasPagar.Add(cp);

            var cr = ContaReceber.Criar(empresaId, null, catReceita.Id, "CR Perf", hoje);
            cr.AdicionarParcela(1, 200m, hoje.AddDays(3));
            cr.Emitir();
            seed.ContasReceber.Add(cr);

            await seed.SaveChangesAsync();
        }

        // ── Medição: conta comandos EF emitidos pela FluxoBucketsAsync ─────────
        int cmdCount = 0;
        await using var measure = NewContext(conn, out _, msg =>
        {
            if (msg.Contains("Executed DbCommand", StringComparison.Ordinal))
                Interlocked.Increment(ref cmdCount);
        });
        measure.SetMobileTenantContext(empresaId);

        var inicio = hoje;
        var fim = hoje.AddDays(23); // 24 buckets diários

        var buckets = await new FluxoCaixaQueries(measure)
            .FluxoBucketsAsync(empresaId, PeriodicidadeFluxo.Diario, inicio, fim);

        // ── Reporta o número real (arquivo garante leitura fora do runner) ─────
        var totalPrevPagar = buckets.Sum(b => b.PrevistoPagar);
        var totalPrevReceber = buckets.Sum(b => b.PrevistoReceber);
        var linha = $"CMD_COUNT={cmdCount} BUCKETS={buckets.Count} PREV_PAGAR={totalPrevPagar} PREV_RECEBER={totalPrevReceber}";
        output.WriteLine(linha);
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "fluxo_perf.txt"), linha); } catch { /* best-effort */ }

        // ── Igualdade funcional: valores batem ────────────────────────────────
        buckets.Count.Should().Be(24);
        totalPrevPagar.Should().Be(100m);
        totalPrevReceber.Should().Be(200m);

        // ── Regressão de performance: queries FIXAS, não 4 por bucket ─────────
        // Antes: ~96 (4 x 24). Depois da otimização: um punhado, independente do nº de buckets.
        cmdCount.Should().BeLessThanOrEqualTo(8,
            $"FluxoBucketsAsync deve agregar por bucket no SQL, não emitir 4 queries por bucket (emitiu {cmdCount})");
    }

    private static EasyStockDbContext NewContext(string conn, out DbContextOptions<EasyStockDbContext> options, Action<string>? log = null)
    {
        var builder = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(conn);
        if (log is not null)
            builder.LogTo(log, LogLevel.Information);
        options = builder.Options;
        return new EasyStockDbContext(options);
    }
}
