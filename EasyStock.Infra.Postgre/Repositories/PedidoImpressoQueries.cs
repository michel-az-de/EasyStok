using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Leitura do impresso do pedido (S49). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010). A janela usa a
/// mesma junção do KDS (<see cref="KdsPedidoQueries"/>): vaga não liberada → janela de entrega.
/// </summary>
public sealed class PedidoImpressoQueries(EasyStockDbContext db) : IPedidoImpressoQueries
{
    private const int TempoPreparoPadrao = 60;

    public async Task<PedidoImpressoLeitura?> ObterAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default)
    {
        var p = await db.Pedidos
            .AsNoTracking()
            .Include(x => x.Itens)
            .Include(x => x.Pagamentos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == pedidoId, ct);
        if (p is null) return null;

        var janela = await (
                from v in db.VagasOcupadas.AsNoTracking()
                join j in db.JanelasEntrega.AsNoTracking() on v.JanelaEntregaId equals j.Id
                where v.PedidoId == p.Id && v.LiberadoEm == null
                select new { v.DataEntrega, j.HoraInicio, j.HoraFim })
            .FirstOrDefaultAsync(ct);

        var empresa = await db.Empresas
            .AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => new { e.Nome, e.NomeFantasia, e.Documento })
            .FirstOrDefaultAsync(ct);

        var vitrine = await db.Storefronts
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresaId)
            .OrderByDescending(s => s.Ativo)
            .ThenBy(s => s.CriadoEm)
            .Select(s => new { s.TituloPublico, s.DominioCustom, s.WhatsappPedidos, s.LogoUrl })
            .FirstOrDefaultAsync(ct);

        var cliente = p.ClienteId is { } clienteId
            ? await db.Clientes
                .AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && c.Id == clienteId)
                .Select(c => new { c.Nome, c.Telefone, c.Endereco, c.Complemento, c.Bairro, c.Cidade, c.Cep })
                .FirstOrDefaultAsync(ct)
            : null;

        var formaCobranca = await db.CobrancasPedido
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.PedidoId == p.Id && c.MetodoPagamento != null)
            .OrderByDescending(c => c.CriadaEm)
            .Select(c => c.MetodoPagamento)
            .FirstOrDefaultAsync(ct);

        var parada = await (
                from pv in db.ParadasViagem.AsNoTracking()
                join v in db.Viagens.AsNoTracking() on pv.ViagemId equals v.Id
                where pv.PedidoId == p.Id && v.EmpresaId == empresaId
                orderby v.CriadaEm descending
                select new { v.EntregadorId, pv.EntregadorNome })
            .FirstOrDefaultAsync(ct);

        PedidoImpressoEntregaLeitura? entrega = null;
        if (parada is not null)
        {
            var entregador = parada.EntregadorId is { } entregadorId
                ? await db.Entregadores
                    .AsNoTracking()
                    .Where(e => e.EmpresaId == empresaId && e.Id == entregadorId)
                    .Select(e => new { e.Tipo, e.Nome })
                    .FirstOrDefaultAsync(ct)
                : null;
            entrega = new PedidoImpressoEntregaLeitura(entregador?.Tipo, entregador?.Nome ?? parada.EntregadorNome);
        }

        var tempoPreparo = await db.ConfiguracoesAtendimento
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => (int?)c.TempoPreparoPadraoMinutos)
            .FirstOrDefaultAsync(ct) ?? TempoPreparoPadrao;

        var nomeCasa = vitrine?.TituloPublico ?? empresa?.NomeFantasia ?? empresa?.Nome ?? string.Empty;

        return new PedidoImpressoLeitura(
            Id: p.Id,
            CriadoEm: p.CriadoEm,
            AlteradoEm: p.AlteradoEm,
            AgendadoParaEm: p.AgendadoParaEm,
            Janela: janela is null ? null : new PedidoImpressoJanelaLeitura(janela.DataEntrega, janela.HoraInicio, janela.HoraFim),
            Casa: new PedidoImpressoCasaLeitura(nomeCasa, empresa?.Documento, vitrine?.DominioCustom, vitrine?.WhatsappPedidos, vitrine?.LogoUrl),
            Cliente: new PedidoImpressoClienteLeitura(
                p.ClienteId,
                string.IsNullOrWhiteSpace(p.ClienteNome) ? cliente?.Nome : p.ClienteNome,
                string.IsNullOrWhiteSpace(p.ClienteTelefone) ? cliente?.Telefone : p.ClienteTelefone,
                cliente?.Endereco,
                cliente?.Complemento,
                p.ClienteApt,
                cliente?.Bairro,
                cliente?.Cidade,
                cliente?.Cep),
            Observacoes: p.Observacoes,
            Total: p.Total.Valor,
            Pagamentos: p.Pagamentos.Select(g => new PedidoImpressoPagamentoLeitura(g.Valor, g.PagoEm, g.Metodo)).ToList(),
            FormaCobranca: formaCobranca,
            Entrega: entrega,
            TempoPreparoPadraoMinutos: tempoPreparo,
            Itens: p.Itens
                .OrderBy(i => i.CriadoEm)
                .Select(i => new PedidoImpressoItemLeitura(i.Nome, i.VariacaoRotuloSnapshot, i.Quantidade, i.PrecoUnitario, i.Subtotal, i.Observacao))
                .ToList());
    }
}
