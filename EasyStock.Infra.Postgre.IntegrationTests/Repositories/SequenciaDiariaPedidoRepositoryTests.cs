using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// S53 (#1283): o número do dia sai de um upsert atômico. Chamadas simultâneas da mesma empresa e dia recebem
/// números distintos e seguidos; outro dia e outra empresa começam do 1; o RLS isola o contador.
/// #1641-5: também na transação real do pagamento (lock do pedido mais upsert), com rollback, índice único e RLS
/// medidos com o papel da API (NOSUPERUSER, NOBYPASSRLS), não com o superusuário que ignora as policies.
/// </summary>
public class SequenciaDiariaPedidoRepositoryTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateOnly Dia = new(2026, 10, 2);

    private async Task<Empresa> NovaEmpresaAsync(string nome, string cnpj)
    {
        var empresa = Empresa.Criar(nome, cnpj);
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        await db.Database.MigrateAsync();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return empresa;
    }

    private async Task<int> ProximoAsync(Guid empresaId, DateOnly dia)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresaId, dia);
    }

    /// <summary>Contexto como a API: papel sujeito ao RLS, interceptor e tenant fixado.</summary>
    private EasyStockDbContext NovoDbComRls(Guid empresaId)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private async Task<List<Guid>> SemearPedidosAsync(Guid empresaId, int quantidade)
    {
        var pedidos = Enumerable.Range(0, quantidade).Select(_ => Pedido.Criar(empresaId)).ToList();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Pedidos.AddRange(pedidos);
        await db.SaveChangesAsync();
        return pedidos.Select(p => p.Id).ToList();
    }

    private async Task<int?> UltimoAsync(Guid empresaId, DateOnly dia)
    {
        // Superusuário (ignora o RLS) e tenant da dona (o filtro global do EF pede o tenant).
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.SequenciasPedidoDia.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Data == dia).Select(s => (int?)s.Ultimo).SingleOrDefaultAsync();
    }

    [SkippableFact]
    public async Task ChamadasSimultaneasNaoRepetemNumero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await NovaEmpresaAsync("Casa da Baba Sequencia", "11222333000181");

        var numeros = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ProximoAsync(empresa.Id, Dia)));

        numeros.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Enumerable.Range(1, 20));
    }

    [SkippableFact]
    public async Task CadaDiaECadaEmpresaComecaDoUm()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var a = await NovaEmpresaAsync("Casa da Baba Dia A", "11222333000262");
        var b = await NovaEmpresaAsync("Casa da Baba Dia B", "11222333000343");

        (await ProximoAsync(a.Id, Dia)).Should().Be(1);
        (await ProximoAsync(a.Id, Dia)).Should().Be(2);
        (await ProximoAsync(a.Id, Dia.AddDays(1))).Should().Be(1, "dia novo, sequência nova");
        (await ProximoAsync(b.Id, Dia)).Should().Be(1, "outra empresa, sequência própria");
    }

    /// <summary>
    /// #1641-5: o caminho do pagamento abre a transação, trava o pedido (<c>FOR UPDATE</c>), pede o número e grava
    /// o pedido. Dez pagamentos simultâneos de pedidos diferentes saem com 1 a 10, sem repetir, e o contador fica em 10.
    /// </summary>
    [SkippableFact]
    public async Task TransacaoComPedidoTravadoNumeraSemRepetir()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await NovaEmpresaAsync("Casa da Baba Transacao", "11222333000424");
        var ids = await SemearPedidosAsync(empresa.Id, 10);

        await Task.WhenAll(ids.Select(async id =>
        {
            await using var db = NovoDbComRls(empresa.Id);
            await using var tx = await db.Database.BeginTransactionAsync();
            await new PedidoRepository(db).TravarAsync(empresa.Id, id);
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == id);
            pedido.DefinirNumeroDoDia(Dia, await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresa.Id, Dia));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }));

        await using var leitura = NovoDbComRls(empresa.Id);
        var numeros = await leitura.Pedidos.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.NumeroDoDia).ToListAsync();
        numeros.Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(n => (int?)n));
        (await UltimoAsync(empresa.Id, Dia)).Should().Be(10);
    }

    /// <summary>
    /// #1641-5: comportamento real do rollback. O upsert roda na transação aberta, então a confirmação desfeita
    /// devolve o número: o contador não avança e o próximo pagamento recebe o mesmo número, sem buraco.
    /// </summary>
    [SkippableFact]
    public async Task RollbackDevolveONumero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await NovaEmpresaAsync("Casa da Baba Rollback", "11222333000505");

        await using (var db = NovoDbComRls(empresa.Id))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            (await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresa.Id, Dia)).Should().Be(1);
            await tx.RollbackAsync();
        }
        (await UltimoAsync(empresa.Id, Dia)).Should().BeNull("a linha do dia nasceu na transação desfeita");

        await using (var db = NovoDbComRls(empresa.Id))
        {
            (await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresa.Id, Dia)).Should().Be(1);
            await using var tx = await db.Database.BeginTransactionAsync();
            (await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresa.Id, Dia)).Should().Be(2);
            await tx.RollbackAsync();
            (await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresa.Id, Dia)).Should().Be(2, "o 2 desfeito volta");
        }
        (await UltimoAsync(empresa.Id, Dia)).Should().Be(2);
    }

    /// <summary>
    /// #1641-5: o índice único <c>(EmpresaId, DataNumero, NumeroDoDia)</c> barra o mesmo número duas vezes no dia da
    /// empresa; outro dia e outra empresa podem repetir.
    /// </summary>
    [SkippableFact]
    public async Task IndiceUnicoBarraNumeroRepetidoNoDia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var a = await NovaEmpresaAsync("Casa da Baba Indice A", "11222333000696");
        var b = await NovaEmpresaAsync("Casa da Baba Indice B", "11222333000777");
        var idsA = await SemearPedidosAsync(a.Id, 3);
        var idsB = await SemearPedidosAsync(b.Id, 1);

        await using (var db = NovoDbComRls(a.Id))
        {
            (await db.Pedidos.SingleAsync(p => p.Id == idsA[0])).DefinirNumeroDoDia(Dia, 5);
            (await db.Pedidos.SingleAsync(p => p.Id == idsA[1])).DefinirNumeroDoDia(Dia.AddDays(1), 5);
            await db.SaveChangesAsync();
        }
        await using (var db = NovoDbComRls(b.Id))
        {
            (await db.Pedidos.SingleAsync(p => p.Id == idsB[0])).DefinirNumeroDoDia(Dia, 5);
            await db.SaveChangesAsync();
        }

        await using var repetido = NovoDbComRls(a.Id);
        (await repetido.Pedidos.SingleAsync(p => p.Id == idsA[2])).DefinirNumeroDoDia(Dia, 5);
        var falha = await FluentActions.Awaiting(() => repetido.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        var postgres = falha.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        postgres.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.Should().Be("ux_pedidos_empresa_numero_do_dia");
    }

    /// <summary>
    /// #1641-5 (negativo de RLS): com o tenant de outra empresa, o contador alheio não aparece e não avança. O
    /// upsert é recusado pelo WITH CHECK da policy (42501) e o contador da dona continua de onde estava.
    /// </summary>
    [SkippableFact]
    public async Task OutraEmpresaNaoLeNemAvancaASequencia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var dona = await NovaEmpresaAsync("Casa da Baba RLS Dona", "11222333000858");
        var intrusa = await NovaEmpresaAsync("Casa da Baba RLS Intrusa", "11222333000939");

        await using (var db = NovoDbComRls(dona.Id))
        {
            var sequencia = new SequenciaDiariaPedidoRepository(db);
            (await sequencia.ProximoAsync(dona.Id, Dia)).Should().Be(1);
            (await sequencia.ProximoAsync(dona.Id, Dia)).Should().Be(2);
        }

        await using (var db = NovoDbComRls(intrusa.Id))
        {
            (await db.SequenciasPedidoDia.CountAsync(s => s.EmpresaId == dona.Id)).Should().Be(0);
            var falha = await FluentActions.Awaiting(() => new SequenciaDiariaPedidoRepository(db).ProximoAsync(dona.Id, Dia))
                .Should().ThrowAsync<PostgresException>();
            falha.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        (await UltimoAsync(dona.Id, Dia)).Should().Be(2, "a outra empresa não mexe no contador da dona");
        await using (var db = NovoDbComRls(dona.Id))
            (await new SequenciaDiariaPedidoRepository(db).ProximoAsync(dona.Id, Dia)).Should().Be(3);
    }
}
