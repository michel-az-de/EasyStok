using EasyStock.Application.Common;
using EasyStock.Application.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

/// <summary>
/// Gate de tradução SQL do Centro de Comando da Frota (issue 623) contra Postgres REAL
/// (Testcontainers) — o que o InMemory NAO cobre: as agregações GROUP BY EmpresaId tem
/// que traduzir no Npgsql, e a agregação de faturas (mapeamento owned/json) so existe
/// aqui. Seed cross-tenant via bypass RLS; query num contexto SuperAdmin (abre o filtro
/// EF) + bypass (abre a policy do Postgres), espelhando como o endpoint admin roda.
/// </summary>
public class FleetOperationQueriesPostgresTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Rollup_da_frota_traduz_e_agrega_em_postgres_real()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        var now = DateTime.UtcNow;
        var alpha = Guid.NewGuid();   // ativo, saudavel
        var bravo = Guid.NewGuid();   // ativo, sem vendas + fatura vencida
        var delta = Guid.NewGuid();   // suspenso (fora do board)

        await SeedAsync(now, alpha, bravo, delta);

        // Contexto de consulta: SuperAdmin (abre o HasQueryFilter) + bypass RLS na conexao.
        var superAdmin = Substitute.For<ICurrentUserAccessor>();
        superAdmin.IsAuthenticated.Returns(true);
        superAdmin.Nivel.Returns(NivelAcesso.SuperAdmin);

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .Options;
        await using var ctx = new EasyStockDbContext(options, superAdmin);
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var r = await new FleetOperationQueries(ctx).ObterAsync(now, maxLinhas: 100);

        r.TotalTenants.Should().Be(2);            // alpha + bravo (delta suspenso fora)
        r.Totals.Suspensos.Should().Be(1);
        r.Totals.MrrAtivo.Should().Be(300m);      // 100 + 200
        r.Totals.VendasHojeTotal.Should().Be(100m);
        r.Totals.FaturasVencidasCount.Should().Be(1);
        r.Totals.FaturasVencidasValor.Should().Be(300m);

        // pior-primeiro: bravo (penalizado por fatura/sem-vendas) antes de alpha.
        r.Tenants.Select(t => t.Nome).Should().ContainInOrder("Bravo", "Alpha");

        var rb = r.Tenants.Single(t => t.Nome == "Bravo");
        rb.FaturaVencida.Should().BeTrue();
        rb.RiscoFlags.Should().Contain(FleetHealthScoring.FlagFaturaVencida);

        var ra = r.Tenants.Single(t => t.Nome == "Alpha");
        ra.VendasHoje.Should().Be(100m);
        ra.VendasCount.Should().Be(2);
        ra.DevicesAtivos.Should().Be(1);
    }

    private async Task SeedAsync(DateTime now, Guid alpha, Guid bravo, Guid delta)
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var pAlpha = new Plano { Id = Guid.NewGuid(), Nome = "Starter", PrecoMensal = 100m, Ativo = true, CriadoEm = now };
        var pBravo = new Plano { Id = Guid.NewGuid(), Nome = "Plus", PrecoMensal = 200m, Ativo = true, CriadoEm = now };
        var pDelta = new Plano { Id = Guid.NewGuid(), Nome = "Starter", PrecoMensal = 99m, Ativo = true, CriadoEm = now };
        seed.Planos.AddRange(pAlpha, pBravo, pDelta);

        seed.Empresas.AddRange(Empresa(alpha, "Alpha"), Empresa(bravo, "Bravo"), Empresa(delta, "Delta"));
        seed.AssinaturasEmpresa.AddRange(
            Assinatura(alpha, pAlpha.Id, StatusAssinatura.Ativa, now),
            Assinatura(bravo, pBravo.Id, StatusAssinatura.Ativa, now),
            Assinatura(delta, pDelta.Id, StatusAssinatura.Suspensa, now));

        // Alpha: 2 vendas hoje (R$100) + 1 device ativo.
        seed.Set<Order>().AddRange(
            Pedido(alpha, OperacaoCriterios.StatusEntregue, 50m, now),
            Pedido(alpha, OperacaoCriterios.StatusEntregue, 50m, now));
        seed.Set<MobileDevice>().Add(Device(alpha, now));

        // Bravo: sem vendas + fatura vencida (R$300).
        var fatura = Fatura.Criar(bravo, "2026-000900",
            new DadosFaturado("Cliente"), new DadosEmissor("EasyStock"),
            OrigemFatura.Avulsa, now.AddDays(-40), now.AddDays(-5));
        fatura.AdicionarItem("Mensalidade", 1m, 300m, TipoItemFatura.Servico);
        fatura.Status = StatusFatura.Vencida;
        seed.Faturas.Add(fatura);

        await seed.SaveChangesAsync();
    }

    private static Empresa Empresa(Guid id, string nome) => new()
    {
        Id = id,
        Nome = nome,
        Documento = id.ToString("N")[..11],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static AssinaturaEmpresa Assinatura(Guid empresaId, Guid planoId, StatusAssinatura status, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        PlanoId = planoId,
        Status = status,
        DataInicio = now.AddDays(-30),
        CriadoEm = now.AddDays(-30),
        AlteradoEm = now,
    };

    private static Order Pedido(Guid empresaId, string status, decimal total, DateTime updatedAt) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ClientSnapshotName = "Cliente",
        Status = status,
        Total = total,
        EmpresaId = empresaId,
        LojaId = Guid.NewGuid(),
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
    };

    private static MobileDevice Device(Guid empresaId, DateTime lastSeenAt) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ApiKeyHash = "hash-" + Guid.NewGuid().ToString("N")[..8],
        EmpresaId = empresaId,
        LojaId = Guid.NewGuid(),
        LastSeenAt = lastSeenAt,
        Revoked = false,
        CreatedAt = lastSeenAt,
        UpdatedAt = lastSeenAt,
    };
}
