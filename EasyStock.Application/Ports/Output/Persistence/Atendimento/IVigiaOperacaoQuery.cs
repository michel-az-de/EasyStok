namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Pedido pronto, para entrega, sem viagem com entregador nem corrida chamada (#2096).</summary>
public sealed record PedidoProntoSemEntregador(Guid PedidoId, string? ClienteNome, DateTime ProntoDesde);

/// <summary>Encomenda em aberto com horário marcado (#2096).</summary>
public sealed record EncomendaEmAberto(Guid PedidoId, string? ClienteNome, DateTime AgendadoParaEm, DateTime? ConfirmadoPeloClienteEm);

/// <summary>Item de lote ainda no freezer (etiquetas sem baixa), perto de vencer ou parado há muito tempo (#2096).</summary>
public sealed record ItemFreezerParaConferir(string Nome, string CodigoLote, DateTime ProduzidoEm, DateTime? ExpiraEm, int Unidades);

/// <summary>Leituras da vigia da operação, sempre de uma empresa só (o job liga o tenant antes).</summary>
public interface IVigiaOperacaoQuery
{
    /// <param name="prontoAteUtc">Só pedidos prontos até este instante (agora menos o tempo da loja).</param>
    Task<IReadOnlyList<PedidoProntoSemEntregador>> ListarProntosSemEntregadorAsync(
        Guid empresaId, DateTime prontoAteUtc, int limite, CancellationToken ct = default);

    /// <summary>Encomendas abertas com horário entre <paramref name="deUtc"/> e <paramref name="ateUtc"/>.</summary>
    Task<IReadOnlyList<EncomendaEmAberto>> ListarEncomendasAsync(
        Guid empresaId, DateTime deUtc, DateTime ateUtc, int limite, CancellationToken ct = default);

    /// <param name="venceAteUtc">Entra o item que vence até aqui (inclui o já vencido). Nulo: não olha validade.</param>
    /// <param name="produzidoAteUtc">Entra o lote produzido até aqui (parado). Nulo: não olha idade.</param>
    Task<IReadOnlyList<ItemFreezerParaConferir>> ListarFreezerParaConferirAsync(
        Guid empresaId, DateTime? venceAteUtc, DateTime? produzidoAteUtc, int limite, CancellationToken ct = default);
}
