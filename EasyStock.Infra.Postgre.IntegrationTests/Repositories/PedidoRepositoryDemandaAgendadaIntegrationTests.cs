using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// M2.5 (#1502): a demanda da sugestão de produção é a soma das porções dos pedidos agendados
/// antes do corte que ainda vão consumir estoque (aguardando, aprovado, preparando). Contra
/// Postgres porque o GroupBy sobre os itens precisa traduzir para SQL.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PedidoRepositoryDemandaAgendadaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Amanha = new(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Corte = new(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task SomaSoOsAgendadosAntesDoCorte_QueAindaVaoConsumirEstoque()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa Demanda", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });

        var lasanha = Guid.NewGuid();
        var nhoque = Guid.NewGuid();
        db.Pedidos.AddRange(
            Pedido(empresaId, StatusPedidoMapper.Aguardando, Amanha, (lasanha, 2), (nhoque, 1)),
            Pedido(empresaId, StatusPedidoMapper.Preparando, Amanha, (lasanha, 1)),
            Pedido(empresaId, StatusPedidoMapper.AprovadoBaba, Amanha, (nhoque, 4)),
            Pedido(empresaId, StatusPedidoMapper.Pronto, Amanha, (lasanha, 5)),
            Pedido(empresaId, StatusPedidoMapper.Entregue, Amanha, (lasanha, 6)),
            Pedido(empresaId, StatusPedidoMapper.AguardandoPagamento, Amanha, (lasanha, 7)),
            Pedido(empresaId, StatusPedidoMapper.Aguardando, Corte, (lasanha, 8)),
            Pedido(empresaId, StatusPedidoMapper.Aguardando, null, (lasanha, 9)));
        await db.SaveChangesAsync();

        var demanda = await new PedidoRepository(db).GetDemandaAgendadaAsync(empresaId, Corte);

        demanda.Select(d => (d.CardapioItemId, d.Quantidade)).Should().BeEquivalentTo([(lasanha, 3m), (nhoque, 5m)],
            "pronto e entregue já baixaram; aguardando pagamento não é firme; no corte ou sem agenda não entra");
    }

    private static Pedido Pedido(Guid empresaId, string status, DateTime? agendadoPara, params (Guid Prato, decimal Qtd)[] itens)
    {
        var pedido = EasyStock.Domain.Entities.Pedido.Criar(empresaId);
        pedido.Status = status;
        pedido.AgendadoParaEm = agendadoPara;
        foreach (var (prato, qtd) in itens)
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, CardapioItemId = prato, Nome = "Prato",
                Quantidade = qtd, PrecoUnitario = 10m, Subtotal = 10m * qtd, CriadoEm = DateTime.UtcNow
            });
        pedido.RecalcularTotal();
        return pedido;
    }
}
