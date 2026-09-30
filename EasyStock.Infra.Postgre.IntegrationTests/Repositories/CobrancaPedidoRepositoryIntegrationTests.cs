using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Persistência da <see cref="CobrancaPedido"/> (S11) em Postgres real: índice único
/// (Provedor, ReferenciaExterna), cobranças do pedido por tenant, as duas consultas cross-tenant
/// (webhook sem JWT e varredura do job) e a migration subindo e descendo limpa com a policy de RLS.
///
/// Fixture por classe: a migration é desfeita e refeita no último cenário.
/// </summary>
public class CobrancaPedidoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string MigrationAnterior = "20260929233026_AddRoteamentoMensageriaMeta";
    private static readonly DateTime Agora = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

    private static async Task<Pedido> CriarPedidoAsync(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    [SkippableFact]
    public async Task ReferenciaUnicaPorProvedor_ENaEntregaSemReferenciaNaoColide()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var pedido = await CriarPedidoAsync(db, empresa);
        var repo = new CobrancaPedidoRepository(db);

        await repo.AddAsync(CobrancaPedido.CriarOnline(empresa, pedido.Id, 25m, "pref-x", "https://mp/x", Agora.AddMinutes(30), 1, Agora));
        await repo.AddAsync(CobrancaPedido.CriarNaEntrega(empresa, pedido.Id, 25m, Agora.AddMinutes(1)));
        await repo.AddAsync(CobrancaPedido.CriarNaEntrega(empresa, pedido.Id, 25m, Agora.AddMinutes(2)));
        await db.SaveChangesAsync();

        var duplicada = CobrancaPedido.CriarOnline(empresa, pedido.Id, 25m, "pref-x", "https://mp/x", Agora.AddMinutes(30), 2, Agora);
        await repo.AddAsync(duplicada);
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("a preferência aponta para uma cobrança só");
        db.Entry(duplicada).State = EntityState.Detached;

        var doPedido = await repo.ListarDoPedidoAsync(empresa, pedido.Id);
        doPedido.Should().HaveCount(3);
        doPedido.First().Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago, "ordenadas da mais antiga para a mais nova");
    }

    [SkippableFact]
    public async Task ConsultasSemTenant_AchamEmpresaEVencidas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        Guid pedidoId, vencidaId;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var pedido = await CriarPedidoAsync(db, empresa);
            pedidoId = pedido.Id;
            var vencida = CobrancaPedido.CriarOnline(empresa, pedido.Id, 25m, $"pref-{Guid.NewGuid():N}", "https://mp/v", Agora.AddMinutes(30), 1, Agora);
            var valida = CobrancaPedido.CriarOnline(empresa, pedido.Id, 25m, $"pref-{Guid.NewGuid():N}", "https://mp/o", Agora.AddHours(5), 1, Agora);
            db.CobrancasPedido.AddRange(vencida, valida);
            await db.SaveChangesAsync();
            vencidaId = vencida.Id;
        }

        await using var semTenant = fixture.CreateDbContext();
        var repo = new CobrancaPedidoRepository(semTenant);

        (await repo.ObterEmpresaIdDoPedidoAsync(pedidoId)).Should().Be(empresa, "o webhook chega sem JWT");
        (await repo.ObterEmpresaIdDoPedidoAsync(Guid.NewGuid())).Should().BeNull();

        var vencidas = await repo.ListarPendentesVencidasAsync(Agora.AddMinutes(31), 50);
        vencidas.Should().Contain(v => v.CobrancaId == vencidaId && v.EmpresaId == empresa && v.PedidoId == pedidoId);
        vencidas.Should().NotContain(v => v.PedidoId == pedidoId && v.CobrancaId != vencidaId, "a de 5 h ainda vale");
    }

    [SkippableFact]
    public async Task IsolamentoDeTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        Guid pedidoB;
        await using (var dbB = fixture.CreateDbContext())
        {
            dbB.SetMobileTenantContext(empresaB);
            var pedido = await CriarPedidoAsync(dbB, empresaB);
            pedidoB = pedido.Id;
            dbB.CobrancasPedido.Add(CobrancaPedido.CriarNaEntrega(empresaB, pedido.Id, 10m, Agora));
            await dbB.SaveChangesAsync();
        }

        await using var dbA = fixture.CreateDbContext();
        dbA.SetMobileTenantContext(empresaA);
        var repo = new CobrancaPedidoRepository(dbA);
        (await repo.ListarDoPedidoAsync(empresaA, pedidoB)).Should().BeEmpty("cobrança de outra empresa é invisível");
        (await dbA.CobrancasPedido.CountAsync(c => c.PedidoId == pedidoB)).Should().Be(0);
    }

    [SkippableFact]
    public async Task VarreduraAntigaDeAbandonados_IgnoraPedidoComCobranca()
    {
        // S11: o CancelarPedidosAbandonadosBackgroundService (30 min da criação) deixa o pedido com
        // CobrancaPedido para o CobrancaPedidoJob, que conta a expiração pelo link.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var comCobranca = await CriarPedidoAsync(db, empresa);
        var semCobranca = Pedido.Criar(empresa, origem: "storefront");
        db.Pedidos.Add(semCobranca);
        foreach (var p in new[] { comCobranca, semCobranca })
        {
            p.Origem = "storefront";
            p.Status = EasyStock.Domain.Sales.StatusPedidoMapper.AguardandoPagamento;
            p.CriadoEm = Agora.AddHours(-2);
        }
        db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(empresa, comCobranca.Id, 25m, $"pref-{Guid.NewGuid():N}",
            "https://mp/a", Agora.AddMinutes(30), 1, Agora));
        await db.SaveChangesAsync();

        var repo = new EasyStock.Infra.Postgre.Repositories.Storefront.PedidoStorefrontRepository(db);
        var expirados = await repo.GetAguardandoPagamentoExpiradosAsync(Agora.AddHours(-1), 500);

        expirados.Select(p => p.Id).Should().Contain(semCobranca.Id).And.NotContain(comCobranca.Id);
    }

    [SkippableFact]
    public async Task Migration_SobeEDesceLimpaComRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        (await ContarTabelaAsync(db)).Should().Be(1);
        (await ContarPolicyAsync(db)).Should().Be(1, "a tabela nova precisa da policy tenant_isolation (ADR-0010)");

        await migrator.MigrateAsync(MigrationAnterior);
        (await ContarTabelaAsync(db)).Should().Be(0, "Down remove a tabela");

        await migrator.MigrateAsync();
        (await ContarTabelaAsync(db)).Should().Be(1);
        (await ContarPolicyAsync(db)).Should().Be(1);
    }

    private static Task<int> ContarTabelaAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'cobrancas_pedido'")
            .SingleAsync();

    private static Task<int> ContarPolicyAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'cobrancas_pedido'")
            .SingleAsync();
}
