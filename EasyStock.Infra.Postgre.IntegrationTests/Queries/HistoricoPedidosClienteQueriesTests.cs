using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Queries;

/// <summary>S25: pedidos do cliente para o dossiê, mais novo primeiro, com itens e só da empresa.</summary>
public class HistoricoPedidosClienteQueriesTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ProjetaPedidosDoClienteMaisNovoPrimeiroComItens()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresa = Empresa.Criar("Casa da Baba Historico", "44444444000191");
        var maria = Cliente.Criar(empresa.Id, "Maria");
        var outro = Cliente.Criar(empresa.Id, "Outro");
        var antigo = NovoPedido(empresa.Id, maria, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), "Bolo", 1m);
        var recente = NovoPedido(empresa.Id, maria, new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), "Brigadeiro", 3m);
        var doOutro = NovoPedido(empresa.Id, outro, new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc), "Torta", 1m);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.Add(empresa);
            db.Clientes.AddRange(maria, outro);
            db.Pedidos.AddRange(antigo, recente, doOutro);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var queries = new HistoricoPedidosClienteQueries(db);

            var pedidos = await queries.ListarAsync(empresa.Id, maria.Id, 10);

            pedidos.Select(p => p.Id).Should().Equal(recente.Id, antigo.Id);
            pedidos[0].Itens.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Nome = "Brigadeiro", Quantidade = 3m });
            pedidos[0].Total.Should().Be(30m);
            pedidos[0].Status.Should().Be(StatusPedidoMapper.Aguardando);
            (await queries.ListarAsync(empresa.Id, maria.Id, 1)).Should().ContainSingle(p => p.Id == recente.Id);
            (await queries.ListarAsync(Guid.NewGuid(), maria.Id, 10)).Should().BeEmpty("EmpresaId no WHERE");
        }
    }

    private static Pedido NovoPedido(Guid empresaId, Cliente cliente, DateTime criadoEm, string item, decimal quantidade)
    {
        var pedido = Pedido.Criar(empresaId, cliente);
        pedido.CriadoEm = criadoEm;
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Nome = item,
            Quantidade = quantidade,
            PrecoUnitario = 10m,
            Subtotal = 10m * quantidade,
            CriadoEm = criadoEm,
        });
        pedido.RecalcularTotal();
        return pedido;
    }
}
