namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Leitura do KDS do console (S19) sobre <see cref="Pedido"/>. Só leitura, sem tracking.
/// </summary>
public interface IKdsPedidoQueries
{
    /// <summary>
    /// Pedidos da empresa nos <paramref name="status"/> cujo dia de produção cai em
    /// [<paramref name="dataInicial"/>, <paramref name="dataFinal"/>] (datas civis de Brasília).
    /// Dia de produção: a data da vaga ativa (<c>VagaOcupada.DataEntrega</c>); sem vaga,
    /// <c>AgendadoParaEm</c>; sem agendamento, <c>CriadoEm</c>. <c>aguardando_aprovacao_baba</c> não tem corte
    /// de data: a aprovação acontece antes do dia de produção.
    /// </summary>
    Task<IReadOnlyList<KdsPedidoLeitura>> ListarAsync(
        Guid empresaId,
        IReadOnlyCollection<string> status,
        DateOnly dataInicial,
        DateOnly dataFinal,
        CancellationToken ct = default);
}

/// <param name="DataProducao">Dia de produção (data civil de Brasília), conforme <see cref="IKdsPedidoQueries.ListarAsync"/>.</param>
/// <param name="InicioPrevistoEm"><c>Pedido.InicioPrevistoEm</c> (S21).</param>
/// <param name="Endereco">Endereço do cadastro do cliente (logradouro, bairro, cidade), para a entrega (S14).</param>
/// <param name="RequerAprovacao"><c>Pedido.RequerAprovacao</c> (S12).</param>
/// <param name="AReceber">Saldo financeiro calculado em lote para os pedidos desta leitura (#1859).</param>
/// <param name="NumeroComanda">#1883: número da conversa dona do pedido ("Comanda N"), pela mesma regra de
/// <c>IConversaRepository.ObterIdPorPedidoAsync</c>, resolvido em lote. Nulo quando nenhuma conversa é dona dele.</param>
/// <param name="NumeroDoDia">#1641-1: número do dia (S53) quando atribuído para o <paramref name="DataProducao"/>; nulo sem número
/// ou com número de outro dia.</param>
public sealed record KdsPedidoLeitura(
    Guid Id,
    string Status,
    string? ClienteNome,
    string? ClienteApt,
    string? Observacoes,
    DateTime? AgendadoParaEm,
    DateTime CriadoEm,
    DateTime? PagoEm,
    DateOnly DataProducao,
    DateTime? InicioPrevistoEm,
    KdsJanelaLeitura? Janela,
    IReadOnlyList<KdsItemLeitura> Itens,
    string? Endereco = null,
    bool RequerAprovacao = false,
    string? MotivoRequerAprovacao = null,
    decimal? AReceber = null,
    long? NumeroComanda = null,
    int? NumeroDoDia = null);

public sealed record KdsJanelaLeitura(string Label, DateOnly Data, TimeOnly Inicio, TimeOnly Fim);

public sealed record KdsItemLeitura(
    string Nome,
    string? Variacao,
    decimal Quantidade,
    string? Observacao,
    string? Linha,
    string? Molho);
