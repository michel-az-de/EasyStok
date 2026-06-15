using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// Fechamento de caixa contra Postgres real (issue #615). Garante que o marcador
/// MovimentoCaixa "fechamento" persiste numa coluna timestamptz: antes do fix ele gravava
/// DataMovimento com Kind=Unspecified (de DateOnly.ToDateTime(TimeOnly.MaxValue)), que o
/// Npgsql 9 rejeita, abortando o CommitAsync inteiro e deixando o caixa "fechado" na verdade
/// aberto. Tambem valida que GetAberturaPendenteAsync (#596) ve a sessao encerrada.
///
/// Mesmo wiring de producao do FinalizarVendaBalcaoIntegrationTests: DI real
/// (AddEasyStockApplication + AddEasyStockPostgreInfrastructure) e EasyStockDbContext scoped.
/// </summary>
public class CaixaFechamentoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task FecharCaixa_persiste_marcador_timestamptz_e_encerra_sessao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();

        // ── Seed: empresa + loja ───────────────────────────────────────────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa Caixa",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId,
                EmpresaId = empresaId,
                Nome = "Loja Caixa",
                Ativa = true,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();

        // ── Abrir caixa ────────────────────────────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var abrir = scope.ServiceProvider.GetRequiredService<AbrirCaixaUseCase>();
            await abrir.ExecuteAsync(new AbrirCaixaCommand(empresaId, 100m, lojaId));
        }

        // ── Fechar caixa: grava FechamentoCaixa + marcador "fechamento". ────
        // Sem o fix (#615), o CommitAsync lancaria por Kind=Unspecified em timestamptz.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var fechar = scope.ServiceProvider.GetRequiredService<FecharCaixaUseCase>();
            var fech = await fechar.ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId));
            fech.Should().NotBeNull();
        }

        // ── Asserts em contexto fresco ─────────────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);
            var hoje = HorarioBrasil.Hoje();
            var caixaRepo = new CaixaRepository(assert);

            // 1. FechamentoCaixa persistiu (a transacao NAO abortou).
            var fechamento = await caixaRepo.GetFechamentoDoDiaAsync(empresaId, hoje, lojaId);
            fechamento.Should().NotBeNull();

            // 2. O marcador "fechamento" persistiu e voltou do Postgres como UTC.
            var marcador = await assert.MovimentosCaixa
                .Where(m => m.EmpresaId == empresaId && m.Tipo == "fechamento")
                .SingleAsync();
            marcador.DataMovimento.Kind.Should().Be(DateTimeKind.Utc);

            // 3. A sessao e vista como encerrada por GetAberturaPendenteAsync (#596).
            var pendente = await caixaRepo.GetAberturaPendenteAsync(empresaId, lojaId);
            pendente.Should().BeNull();
        }
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
