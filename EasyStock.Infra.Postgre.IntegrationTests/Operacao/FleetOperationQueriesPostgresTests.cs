using EasyStock.Application.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

/// <summary>
/// Gate de produção da tela Operação (issue 623) contra Postgres REAL (Testcontainers).
/// Prova o que quebrava em prod: a VENDA do ERP (db.Vendas) é protegida por RLS, e a query
/// só a enxerga cross-tenant porque chama <c>UseRowLevelSecurityBypass</c> e o interceptor
/// emite <c>SET app.bypass_rls</c>. Sem isso, VendasHojeTotal volta 0 (o sintoma do bug).
/// </summary>
public class FleetOperationQueriesPostgresTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Le_venda_real_do_ERP_cross_tenant_via_bypass_de_RLS()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        var now = DateTime.UtcNow;
        var alpha = Guid.NewGuid();   // ativo, vendeu hoje -> saudável
        var bravo = Guid.NewGuid();   // ativo, sem vendas + fatura vencida -> crítico

        await SeedAsync(now, alpha, bravo);

        // Contexto de consulta: SuperAdmin (abre o filtro EF) + interceptor (pra o bypass
        // de RLS chamado dentro da query realmente emitir SET app.bypass_rls).
        var superAdmin = Substitute.For<ICurrentUserAccessor>();
        superAdmin.IsAuthenticated.Returns(true);
        superAdmin.Nivel.Returns(NivelAcesso.SuperAdmin);

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        await using var ctx = new EasyStockDbContext(options, superAdmin);

        var r = await new FleetOperationQueries(ctx).ObterAsync(now, maxLinhas: 100);

        r.TotalClientes.Should().Be(2);
        r.Totais.ClientesAtivos.Should().Be(2);
        r.Totais.MrrAtivo.Should().Be(300m);              // 100 + 200
        r.Totais.VendasHojeTotal.Should().Be(500m);       // <- prova o bypass: venda do ERP cross-tenant
        r.Totais.FaturasVencidasValor.Should().Be(300m);

        // pior-primeiro: Bravo (crítico por fatura vencida) antes de Alpha (ok).
        r.Clientes.Select(c => c.Nome).Should().ContainInOrder("Bravo", "Alpha");

        var rb = r.Clientes.Single(c => c.Nome == "Bravo");
        rb.StatusBand.Should().Be(FleetHealthScoring.BandCrit);
        rb.FaturasVencidasCount.Should().Be(1);
        rb.Motivos.Should().Contain(FleetHealthScoring.MotivoFaturaVencida);

        var ra = r.Clientes.Single(c => c.Nome == "Alpha");
        ra.StatusBand.Should().Be(FleetHealthScoring.BandOk);
        ra.VendasHoje.Should().Be(500m);
    }

    private async Task SeedAsync(DateTime now, Guid alpha, Guid bravo)
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var pAlpha = new Plano { Id = Guid.NewGuid(), Nome = "Starter", PrecoMensal = 100m, Ativo = true, CriadoEm = now };
        var pBravo = new Plano { Id = Guid.NewGuid(), Nome = "Plus", PrecoMensal = 200m, Ativo = true, CriadoEm = now };
        seed.Planos.AddRange(pAlpha, pBravo);

        seed.Empresas.AddRange(Empresa(alpha, "Alpha"), Empresa(bravo, "Bravo"));
        seed.AssinaturasEmpresa.AddRange(
            Assinatura(alpha, pAlpha.Id, now),
            Assinatura(bravo, pBravo.Id, now));

        // Alpha: venda real do ERP hoje (R$500).
        seed.Vendas.Add(new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = alpha,
            ValorTotal = Dinheiro.FromDecimal(500m),
            DataVenda = now,
            CriadoEm = now,
        });

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

    private static AssinaturaEmpresa Assinatura(Guid empresaId, Guid planoId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        PlanoId = planoId,
        Status = StatusAssinatura.Ativa,
        DataInicio = now.AddDays(-30),
        CriadoEm = now.AddDays(-30),
        AlteradoEm = now,
    };
}
