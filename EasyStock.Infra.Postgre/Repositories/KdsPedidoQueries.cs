using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;

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
        // Aguardando aprovação não tem corte (#1238): a dona aprova hoje o pedido de daqui a 3 dias.
        var pedidos = await db.Pedidos
            .AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Where(p => p.EmpresaId == empresaId && statusLista.Contains(p.Status))
            .Where(p =>
                p.Status == StatusPedidoMapper.AguardandoAprovacaoBaba
                || db.VagasOcupadas.Any(v => v.PedidoId == p.Id && v.LiberadoEm == null
                                          && v.DataEntrega >= dataInicial && v.DataEntrega <= dataFinal)
                || (!db.VagasOcupadas.Any(v => v.PedidoId == p.Id && v.LiberadoEm == null)
                    && (p.AgendadoParaEm ?? p.CriadoEm) >= iniUtc
                    && (p.AgendadoParaEm ?? p.CriadoEm) < fimUtc))
            // #1709-1: o corte guarda os mais novos (o pedido que acabou de entrar não some); a tela segue do mais
            // antigo para o mais novo.
            .OrderByDescending(p => p.CriadoEm)
            .Take(Limite)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (pedidos.Count == 0) return [];
        pedidos = pedidos.OrderBy(p => p.CriadoEm).ToList();

        var ids = pedidos.Select(p => p.Id).ToList();
        var comandas = await NumeroComandaDoPedido.PorPedidosAsync(db, empresaId, ids, ct);
        var janelas = (await (
                from v in db.VagasOcupadas.AsNoTracking()
                join j in db.JanelasEntrega.AsNoTracking() on v.JanelaEntregaId equals j.Id
                where ids.Contains(v.PedidoId) && v.LiberadoEm == null
                select new { v.PedidoId, v.DataEntrega, j.Label, j.HoraInicio, j.HoraFim })
            .ToListAsync(ct))
            .GroupBy(x => x.PedidoId)
            .ToDictionary(g => g.Key, g => g.First());

        // Endereço do cadastro do cliente (S14), com EmpresaId no WHERE como o resto da leitura. O nome do
        // cadastro cobre o pedido que nasceu sem o retrato do cliente (#1474).
        var clienteIds = pedidos.Where(p => p.ClienteId != null).Select(p => p.ClienteId!.Value).Distinct().ToList();
        var clientes = clienteIds.Count == 0
            ? new Dictionary<Guid, ClienteDoPedido>()
            : (await db.Clientes
                .AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && clienteIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Nome, c.Endereco, c.Bairro, c.Cidade })
                .ToListAsync(ct))
                .ToDictionary(c => c.Id, c => new ClienteDoPedido(c.Nome, EnderecoEmTexto(c.Endereco, c.Bairro, c.Cidade)));

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

        var saldos = await SaldosAsync(empresaId, pedidos, ct);

        return pedidos.Select(p =>
        {
            var janela = janelas.GetValueOrDefault(p.Id);
            var cliente = p.ClienteId is { } cid ? clientes.GetValueOrDefault(cid) : null;
            return new KdsPedidoLeitura(
                Id: p.Id,
                Status: p.Status,
                ClienteNome: string.IsNullOrWhiteSpace(p.ClienteNome) ? cliente?.Nome : p.ClienteNome,
                ClienteApt: p.ClienteApt,
                Observacoes: p.Observacoes,
                AgendadoParaEm: p.AgendadoParaEm,
                CriadoEm: p.CriadoEm,
                PagoEm: p.Pagamentos.Count == 0 ? null : p.Pagamentos.Max(g => g.PagoEm),
                DataProducao: janela?.DataEntrega ?? HorarioBrasil.DataOperacional(p.AgendadoParaEm ?? p.CriadoEm),
                InicioPrevistoEm: p.InicioPrevistoEm,
                Janela: janela is null ? null : new KdsJanelaLeitura(janela.Label, janela.DataEntrega, janela.HoraInicio, janela.HoraFim),
                Itens: p.Itens
                    .Where(i => !i.EhLinhaDeFrete)
                    .OrderBy(i => i.CriadoEm)
                    .Select(i => new KdsItemLeitura(
                        Nome: NomeCardapio.Exibicao(i.Nome) ?? i.Nome,
                        Variacao: i.VariacaoRotuloSnapshot,
                        Quantidade: i.Quantidade,
                        Observacao: i.Observacao,
                        Linha: i.LinhaSnapshot,
                        Molho: i.CardapioItemId is { } c ? molhos.GetValueOrDefault(c) : null))
                    .ToList(),
                Endereco: p.EnderecoEntrega ?? cliente?.Endereco,
                RequerAprovacao: p.RequerAprovacao,
                MotivoRequerAprovacao: p.MotivoRequerAprovacao,
                AReceber: saldos[p.Id].AReceber(p.Total.Valor),
                NumeroComanda: comandas.TryGetValue(p.Id, out var comanda) ? comanda : null,
                NumeroDoDia: p.NumeroDoDia,
                ConfirmadoPeloClienteEm: p.ConfirmadoPeloClienteEm);
        }).ToList();
    }

    // #1859: cinco consultas para toda a página, sempre com EmpresaId e os IDs já selecionados.
    // O legado só conta se nenhum estorno online representa aquele pagamento, como na leitura individual.
    private async Task<Dictionary<Guid, SaldoFinanceiroComanda>> SaldosAsync(
        Guid empresaId, IReadOnlyList<Pedido> pedidos, CancellationToken ct)
    {
        var ids = pedidos.Select(p => p.Id).ToList();
        var online = await db.Set<PedidoEstornoOnline>().AsNoTracking()
            .Where(e => e.EmpresaId == empresaId && ids.Contains(e.PedidoId))
            .GroupBy(e => e.PedidoId)
            .Select(g => new
            {
                PedidoId = g.Key,
                Devolvido = g.Sum(e => e.Situacao == PedidoEstornoOnline.Confirmado ? e.Valor : 0m),
                Reservado = g.Sum(e => e.Situacao == PedidoEstornoOnline.Pendente ? e.Valor : 0m)
            })
            .ToDictionaryAsync(e => e.PedidoId, ct);
        var legado = await (from p in db.Pedidos.AsNoTracking()
                            from r in p.Pagamentos
                            join c in db.CobrancasPedido.AsNoTracking() on new { PedidoId = p.Id, Referencia = r.Referencia } equals
                                new { c.PedidoId, Referencia = c.PagamentoExternoId }
                            where p.EmpresaId == empresaId && ids.Contains(p.Id) && c.EmpresaId == empresaId
                                && c.Provedor == CobrancaPedido.ProvedorMercadoPago && c.Status == StatusCobrancaPedido.Estornada
                                && !db.Set<PedidoEstornoOnline>().Any(e => e.EmpresaId == empresaId && e.PedidoId == p.Id && e.PagamentoId == r.Id)
                            group r by p.Id into g
                            select new { PedidoId = g.Key, Valor = g.Sum(r => r.Valor) })
            .ToDictionaryAsync(e => e.PedidoId, e => e.Valor, ct);
        var manual = await db.Set<PedidoEstornoManual>().AsNoTracking()
            .Where(e => e.EmpresaId == empresaId && ids.Contains(e.PedidoId))
            .GroupBy(e => e.PedidoId)
            .Select(g => new { PedidoId = g.Key, Valor = g.Sum(e => e.Valor) })
            .ToDictionaryAsync(e => e.PedidoId, e => e.Valor, ct);
        var emitido = await db.Set<CreditoClienteComanda>().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && ids.Contains(c.PedidoOrigemId))
            .GroupBy(c => c.PedidoOrigemId)
            .Select(g => new { PedidoId = g.Key, Valor = g.Sum(c => c.Valor) })
            .ToDictionaryAsync(e => e.PedidoId, e => e.Valor, ct);
        var utilizado = await db.Set<UsoCreditoClienteComanda>().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && ids.Contains(c.PedidoId))
            .GroupBy(c => c.PedidoId)
            .Select(g => new { PedidoId = g.Key, Valor = g.Sum(c => c.Valor) })
            .ToDictionaryAsync(e => e.PedidoId, e => e.Valor, ct);

        return pedidos.ToDictionary(p => p.Id, p => new SaldoFinanceiroComanda(p.TotalPago,
            legado.GetValueOrDefault(p.Id) + (online.GetValueOrDefault(p.Id)?.Devolvido ?? 0m) + manual.GetValueOrDefault(p.Id),
            emitido.GetValueOrDefault(p.Id), utilizado.GetValueOrDefault(p.Id), online.GetValueOrDefault(p.Id)?.Reservado ?? 0m));
    }

    private sealed record ClienteDoPedido(string? Nome, string? Endereco);

    /// <summary>Mesmo formato do despacho da viagem (S44): sem logradouro, sem endereço.</summary>
    private static string? EnderecoEmTexto(string? endereco, string? bairro, string? cidade)
    {
        if (string.IsNullOrWhiteSpace(endereco)) return null;
        return string.Join(", ", new[] { endereco, bairro, cidade }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
    }
}
