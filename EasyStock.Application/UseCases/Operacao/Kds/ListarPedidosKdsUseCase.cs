using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Operacao.Kds;

/// <param name="Status">Lista separada por vírgula (<c>aguardando,preparando</c>). Vazio ou só inválidos = padrão:
/// <c>aprovado_baba</c> (#2055), <c>aguardando</c>, <c>preparando</c>, <c>pronto</c> e <c>saiu_para_entrega</c>.</param>
/// <param name="Linha">Filtro por linha de item; aceita <c>prepararEmCasa</c> ou <c>preparar_em_casa</c>.</param>
/// <param name="Data">Dia de produção. Nulo = hoje e os abertos de ontem.</param>
public sealed record ListarPedidosKdsQuery(
    Guid EmpresaId,
    string? Status = null,
    string? Linha = null,
    DateOnly? Data = null) : ICommand;

/// <summary>
/// Lista os cards do KDS do console (S19) a partir de <see cref="Pedido"/>, não do espelho mobile.
/// </summary>
public class ListarPedidosKdsUseCase(IKdsPedidoQueries queries, TimeProvider relogio)
{
    // #2055: aprovado_baba é o pedido fora da área pago e aprovado pela dona; espera o preparo como o aguardando.
    private static readonly string[] StatusPadrao =
    [
        StatusPedidoMapper.AprovadoBaba,
        StatusPedidoMapper.Aguardando,
        StatusPedidoMapper.Preparando,
        StatusPedidoMapper.Pronto,
        StatusPedidoMapper.SaiuParaEntrega,
    ];

    public async Task<IReadOnlyList<KdsPedidoDto>> ExecuteAsync(ListarPedidosKdsQuery query, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(query.EmpresaId);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var hoje = HorarioBrasil.DataOperacional(agora);
        // Padrão: hoje e os que ficaram abertos de ontem (os status padrão não são terminais).
        var (dataInicial, dataFinal) = query.Data is { } d ? (d, d) : (hoje.AddDays(-1), hoje);

        var pedidos = await queries.ListarAsync(query.EmpresaId, ParsearStatus(query.Status), dataInicial, dataFinal, ct);

        var linha = NormalizarLinha(query.Linha);
        return pedidos
            .Where(p => linha is null || p.Itens.Any(i => NormalizarLinha(i.Linha) == linha))
            .Select(p => Mapear(p, hoje, agora) with { AReceber = p.AReceber })
            .ToList();
    }

    private static IReadOnlyCollection<string> ParsearStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return StatusPadrao;
        var lista = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => StatusPedidoMapper.TryParse(s, out var st) ? StatusPedidoMapper.Format(st) : null)
            .OfType<string>()
            .Distinct()
            .ToList();
        return lista.Count == 0 ? StatusPadrao : lista;
    }

    /// <summary>
    /// S21: com início previsto, atrasado = ainda na fila (aguardando ou, #2055, aprovado pela dona) e o
    /// início já passou (mesma regra de <see cref="Pedido.EstaAtrasado"/>). Sem ele (pedido para já ou
    /// anterior à S21), vale a regra da S19: aberto de dia de produção anterior a hoje.
    /// </summary>
    private static bool Atrasado(KdsPedidoLeitura p, StatusPedido status, DateOnly hoje, DateTime agora) =>
        p.InicioPrevistoEm is { } inicio
            ? status is StatusPedido.Aguardando or StatusPedido.AprovadoBaba && agora > inicio
            : p.DataProducao < hoje && !StatusPedidoVocabulario.EhTerminal(status);

    /// <summary>"preparar_em_casa", "prepararEmCasa" e "PREPARAR-EM-CASA" viram a mesma chave.</summary>
    private static string? NormalizarLinha(string? linha) =>
        string.IsNullOrWhiteSpace(linha)
            ? null
            : new string(linha.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static KdsPedidoDto Mapear(KdsPedidoLeitura p, DateOnly hoje, DateTime agora)
    {
        var status = StatusPedidoMapper.Parse(p.Status);
        return new KdsPedidoDto(
            Id: p.Id,
            NumeroCurto: CodigoCurto.De(p.Id),
            ClienteNome: p.ClienteNome,
            ClienteApt: p.ClienteApt,
            Janela: p.Janela is { } j ? new KdsJanelaDto(j.Label, j.Data, j.Inicio, j.Fim) : null,
            AgendadoParaEm: p.AgendadoParaEm,
            Status: p.Status,
            StatusRotulo: StatusPedidoVocabulario.RotuloLojista(status),
            Linhas: p.Itens.Select(i => i.Linha).OfType<string>().Distinct().ToList(),
            Itens: p.Itens.Select(i => new KdsItemDto(i.Nome, i.Variacao, i.Quantidade, i.Observacao, i.Linha, i.Molho)).ToList(),
            InicioPrevistoEm: p.InicioPrevistoEm,
            Atrasado: Atrasado(p, status, hoje, agora),
            PagoEm: p.PagoEm,
            CriadoEm: p.CriadoEm,
            Observacoes: p.Observacoes,
            Endereco: p.Endereco,
            RequerAprovacao: p.RequerAprovacao,
            MotivoRequerAprovacao: p.MotivoRequerAprovacao,
            NumeroComanda: p.NumeroComanda,
            NumeroDoDia: p.NumeroDoDia);
    }
}
