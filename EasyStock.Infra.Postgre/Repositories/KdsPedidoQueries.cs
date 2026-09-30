using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Leitura do KDS do console (S19). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// </summary>
public sealed class KdsPedidoQueries(EasyStockDbContext db) : IKdsPedidoQueries
{
    /// <summary>Teto de cards por consulta: a cozinha não opera mais que isso num dia.</summary>
    private const int Limite = 200;

    public async Task<IReadOnlyList<KdsPedidoLeitura>> ListarAsync(
        Guid empresaId,
        IReadOnlyCollection<string> status,
        DateOnly dataInicial,
        DateOnly dataFinal,
        CancellationToken ct = default)
    {
        var statusLista = status.ToList();
        var (iniUtc, _) = HorarioBrasil.JanelaDiaUtc(dataInicial);
        var (_, fimUtc) = HorarioBrasil.JanelaDiaUtc(dataFinal);

        // Dia de produção: vaga ativa manda (pedido do storefront); sem vaga, agendamento; sem ele, criação.
        var pedidos = await db.Pedidos
            .AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Where(p => p.EmpresaId == empresaId && statusLista.Contains(p.Status))
            .Where(p =>
                db.VagasOcupadas.Any(v => v.PedidoId == p.Id && v.LiberadoEm == null
                                          && v.DataEntrega >= dataInicial && v.DataEntrega <= dataFinal)
                || (!db.VagasOcupadas.Any(v => v.PedidoId == p.Id && v.LiberadoEm == null)
                    && (p.AgendadoParaEm ?? p.CriadoEm) >= iniUtc
                    && (p.AgendadoParaEm ?? p.CriadoEm) < fimUtc))
            .OrderBy(p => p.CriadoEm)
            .Take(Limite)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (pedidos.Count == 0) return [];

        var ids = pedidos.Select(p => p.Id).ToList();
        var janelas = (await (
                from v in db.VagasOcupadas.AsNoTracking()
                join j in db.JanelasEntrega.AsNoTracking() on v.JanelaEntregaId equals j.Id
                where ids.Contains(v.PedidoId) && v.LiberadoEm == null
                select new { v.PedidoId, v.DataEntrega, j.Label, j.HoraInicio, j.HoraFim })
            .ToListAsync(ct))
            .GroupBy(x => x.PedidoId)
            .ToDictionary(g => g.Key, g => g.First());

        var cardapioIds = pedidos
            .SelectMany(p => p.Itens)
            .Where(i => i.CardapioItemId != null)
            .Select(i => i.CardapioItemId!.Value)
            .Distinct()
            .ToList();
        var molhos = cardapioIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.CardapioItens
                .AsNoTracking()
                .Where(c => cardapioIds.Contains(c.Id) && c.SugestaoMolho != null)
                .ToDictionaryAsync(c => c.Id, c => c.SugestaoMolho, ct);

        return pedidos.Select(p =>
        {
            var janela = janelas.GetValueOrDefault(p.Id);
            return new KdsPedidoLeitura(
                Id: p.Id,
                Status: p.Status,
                ClienteNome: p.ClienteNome,
                ClienteApt: p.ClienteApt,
                Observacoes: p.Observacoes,
                AgendadoParaEm: p.AgendadoParaEm,
                CriadoEm: p.CriadoEm,
                PagoEm: p.Pagamentos.Count == 0 ? null : p.Pagamentos.Max(g => g.PagoEm),
                DataProducao: janela?.DataEntrega ?? HorarioBrasil.DataOperacional(p.AgendadoParaEm ?? p.CriadoEm),
                Janela: janela is null ? null : new KdsJanelaLeitura(janela.Label, janela.DataEntrega, janela.HoraInicio, janela.HoraFim),
                Itens: p.Itens
                    .OrderBy(i => i.CriadoEm)
                    .Select(i => new KdsItemLeitura(
                        Nome: i.Nome,
                        Variacao: i.VariacaoRotuloSnapshot,
                        Quantidade: i.Quantidade,
                        Observacao: i.Observacao,
                        Linha: i.LinhaSnapshot,
                        Molho: i.CardapioItemId is { } c ? molhos.GetValueOrDefault(c) : null))
                    .ToList());
        }).ToList();
    }
}
