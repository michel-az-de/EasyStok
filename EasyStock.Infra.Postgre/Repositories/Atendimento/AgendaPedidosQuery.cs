using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class AgendaPedidosQuery(EasyStockDbContext db) : IAgendaPedidosQuery
{
    public async Task<IReadOnlyList<AgendaPedidoLeitura>> ListarAsync(
        Guid empresaId, Guid storefrontId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var linhas = await (
            from vaga in db.VagasOcupadas.AsNoTracking()
            join janela in db.JanelasEntrega.AsNoTracking() on vaga.JanelaEntregaId equals janela.Id
            join pedido in db.Pedidos.AsNoTracking() on vaga.PedidoId equals pedido.Id
            where janela.StorefrontId == storefrontId && pedido.EmpresaId == empresaId
                && vaga.LiberadoEm == null && vaga.DataEntrega >= de && vaga.DataEntrega <= ate
            orderby vaga.DataEntrega, janela.HoraInicio, pedido.CriadoEm
            select new { JanelaId = janela.Id, Data = vaga.DataEntrega, PedidoId = pedido.Id,
                pedido.ClienteNome, pedido.Status })
            .ToListAsync(ct);

        return linhas.Select(l => new AgendaPedidoLeitura(l.JanelaId, l.Data, l.PedidoId, l.ClienteNome, l.Status)).ToList();
    }
}
