using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>Leituras da vigia da operação (#2096). Só lê: viagem, despacho e cotação seguem com os próprios fluxos.</summary>
public sealed class VigiaOperacaoQuery(EasyStockDbContext db) : IVigiaOperacaoQuery
{
    private static readonly string[] Finalizados =
    [
        StatusPedidoMapper.Entregue, StatusPedidoMapper.Cancelado, StatusPedidoMapper.Rascunho,
        // Pix não pago, carrinho abandonado e exceção que a dona ainda não aprovou não são compromisso da cozinha.
        StatusPedidoMapper.AguardandoPagamento, StatusPedidoMapper.AguardandoAprovacaoBaba,
    ];

    // Corrida que não leva mais o pedido: cancelada, falhou, voltando ou já entregue.
    private static readonly string[] DespachoEncerrado = ["cancelado", "falha", "devolvendo", "entregue"];

    public async Task<IReadOnlyList<PedidoProntoSemEntregador>> ListarProntosSemEntregadorAsync(
        Guid empresaId, DateTime prontoDesdeUtc, DateTime prontoAteUtc, int limite, CancellationToken ct = default)
    {
        var viagensAtivas = db.Viagens.Where(v => v.EmpresaId == empresaId
            && (v.Situacao == SituacaoViagem.Montando || v.Situacao == SituacaoViagem.EmRota));

        var linhas = await db.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId
                && p.Status == StatusPedidoMapper.Pronto
                && p.EnderecoEntrega != null && p.EnderecoEntrega != "")
            // Atendido: viagem ativa com entregador, ou viagem com corrida chamada (Lalamove) ainda de pé.
            .Where(p => !viagensAtivas.Any(v => v.Paradas.Any(pa => pa.PedidoId == p.Id)
                && (v.EntregadorId != null
                    || db.Set<DespachoLogistica>().Any(d => d.EmpresaId == empresaId && d.ViagemId == v.Id
                        && !DespachoEncerrado.Contains(d.Status)))))
            .Select(p => new
            {
                p.Id,
                p.ClienteNome,
                ProntoDesde = db.Set<PedidoEvento>()
                    .Where(e => e.PedidoId == p.Id && e.StatusNovo == StatusPedidoMapper.Pronto)
                    .Max(e => (DateTime?)e.OcorridoEm) ?? p.AlteradoEm,
            })
            .Where(x => x.ProntoDesde <= prontoAteUtc && x.ProntoDesde >= prontoDesdeUtc)
            .OrderByDescending(x => x.ProntoDesde)
            .Take(limite)
            .ToListAsync(ct);

        return linhas.Select(x => new PedidoProntoSemEntregador(x.Id, x.ClienteNome, x.ProntoDesde)).ToList();
    }

    public async Task<IReadOnlyList<EncomendaEmAberto>> ListarEncomendasAsync(
        Guid empresaId, DateTime deUtc, DateTime ateUtc, int limite, CancellationToken ct = default)
    {
        var linhas = await db.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId
                && p.AgendadoParaEm != null && p.AgendadoParaEm > deUtc && p.AgendadoParaEm <= ateUtc
                && !Finalizados.Contains(p.Status))
            .OrderBy(p => p.AgendadoParaEm)
            .Take(limite)
            .Select(p => new { p.Id, p.ClienteNome, p.AgendadoParaEm, p.ConfirmadoPeloClienteEm, p.CriadoEm })
            .ToListAsync(ct);

        return linhas.Select(x => new EncomendaEmAberto(x.Id, x.ClienteNome, x.AgendadoParaEm!.Value, x.ConfirmadoPeloClienteEm, x.CriadoEm))
            .ToList();
    }

    public async Task<IReadOnlyList<ItemFreezerParaConferir>> ListarFreezerParaConferirAsync(
        Guid empresaId, DateTime? venceAteUtc, DateTime? produzidoAteUtc, int limite, CancellationToken ct = default)
    {
        if (venceAteUtc is null && produzidoAteUtc is null) return [];

        // Etiqueta sem baixa = unidade que o sistema acha que ainda está no freezer.
        var linhas = await db.Set<LoteEtiqueta>().AsNoTracking()
            .Where(e => e.Status != LoteEtiquetaStatus.Consumida && e.Status != LoteEtiquetaStatus.Divergente)
            .Join(db.Set<LoteItem>(), e => e.LoteItemId, i => i.Id, (e, i) => new { e, i })
            .Join(db.Lotes, x => x.i.LoteId, l => l.Id, (x, l) => new { x.e, x.i, l })
            .Where(x => x.l.EmpresaId == empresaId && x.l.Status != "em_producao")
            .Where(x => (venceAteUtc != null && x.i.ExpiraEm != null && x.i.ExpiraEm <= venceAteUtc)
                || (produzidoAteUtc != null && x.l.DataProducao <= produzidoAteUtc))
            .GroupBy(x => new { x.i.Id, x.i.Nome, x.l.Codigo, x.l.DataProducao, x.i.ExpiraEm })
            .Select(g => new { g.Key.Nome, g.Key.Codigo, g.Key.DataProducao, g.Key.ExpiraEm, Unidades = g.Count() })
            .OrderBy(x => x.ExpiraEm ?? x.DataProducao)
            .Take(limite)
            .ToListAsync(ct);

        return linhas.Select(x => new ItemFreezerParaConferir(x.Nome, x.Codigo, x.DataProducao, x.ExpiraEm, x.Unidades)).ToList();
    }
}
