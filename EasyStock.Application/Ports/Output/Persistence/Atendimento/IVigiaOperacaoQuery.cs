namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Pedido pronto, para entrega, sem viagem com entregador nem corrida chamada (#2096).</summary>
public sealed record PedidoProntoSemEntregador(Guid PedidoId, string? ClienteNome, DateTime ProntoDesde);

/// <summary>Encomenda em aberto com horário marcado (#2096).</summary>
public sealed record EncomendaEmAberto(
    Guid PedidoId, string? ClienteNome, DateTime AgendadoParaEm, DateTime? ConfirmadoPeloClienteEm, DateTime CriadoEm);

/// <summary>Item de lote ainda no freezer (etiquetas sem baixa), perto de vencer ou parado há muito tempo (#2096).</summary>
public sealed record ItemFreezerParaConferir(string Nome, string CodigoLote, DateTime ProduzidoEm, DateTime? ExpiraEm, int Unidades);

/// <summary>
/// #2836: pedido em aberto com o retrato do cadastro ligado (nulo quando o pedido não tem cliente). Entrega é o pedido com
/// endereço; retirada não pede endereço confirmado.
/// </summary>
public sealed record PedidoAbertoComCliente(
    Guid PedidoId, string? ClienteNomePedido, Guid? ClienteId, string? NomeCadastro, string? TelefoneCadastro,
    string? FotoChave, string? EnderecoEntrega, DateTime? EnderecoConfirmadoEm, string? Origem = null);

/// <summary>Leituras da vigia da operação, sempre de uma empresa só (o job liga o tenant antes).</summary>
public interface IVigiaOperacaoQuery
{
    /// <param name="prontoDesdeUtc">Só pedidos prontos a partir daqui: o parado há dias não vira aviso no deploy.</param>
    /// <param name="prontoAteUtc">Só pedidos prontos até este instante (agora menos o tempo da loja).</param>
    /// <remarks>Mais novos primeiro: o limite nunca esconde o pedido que acabou de estourar.</remarks>
    Task<IReadOnlyList<PedidoProntoSemEntregador>> ListarProntosSemEntregadorAsync(
        Guid empresaId, DateTime prontoDesdeUtc, DateTime prontoAteUtc, int limite, CancellationToken ct = default);

    /// <summary>
    /// Pedidos abertos e já aceitos (fora rascunho, aguardando pagamento e aguardando aprovação) com horário entre
    /// <paramref name="deUtc"/> e <paramref name="ateUtc"/>. Quem decide se é encomenda é a vigia.
    /// </summary>
    Task<IReadOnlyList<EncomendaEmAberto>> ListarEncomendasAsync(
        Guid empresaId, DateTime deUtc, DateTime ateUtc, int limite, CancellationToken ct = default);

    /// <param name="venceAteUtc">Entra o item que vence até aqui (inclui o já vencido). Nulo: não olha validade.</param>
    /// <param name="produzidoAteUtc">Entra o lote produzido até aqui (parado). Nulo: não olha idade.</param>
    Task<IReadOnlyList<ItemFreezerParaConferir>> ListarFreezerParaConferirAsync(
        Guid empresaId, DateTime? venceAteUtc, DateTime? produzidoAteUtc, int limite, CancellationToken ct = default);

    /// <summary>#2836: pedidos abertos e já aceitos, feitos a partir de <paramref name="criadoDesdeUtc"/>, mais novos primeiro.</summary>
    Task<IReadOnlyList<PedidoAbertoComCliente>> ListarPedidosAbertosComClienteAsync(
        Guid empresaId, DateTime criadoDesdeUtc, int limite, CancellationToken ct = default);
}
