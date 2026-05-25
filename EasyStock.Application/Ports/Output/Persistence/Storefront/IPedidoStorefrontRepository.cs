using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>
/// Repo especializado para operações de Pedido no contexto Storefront (ADR-0014).
/// Diferente do <see cref="IPedidoRepository"/> (ERP, escopo por empresaId), este
/// não filtra por tenant — necessário para o background service que varre todos os
/// pedidos <c>AguardandoPagamento</c> expirados.
/// </summary>
public interface IPedidoStorefrontRepository
{
    Task<Pedido?> GetByIdAsync(Guid pedidoId, CancellationToken ct = default);

    Task AddAsync(Pedido pedido, CancellationToken ct = default);

    Task UpdateAsync(Pedido pedido, CancellationToken ct = default);

    Task AddItemAsync(PedidoItem item, CancellationToken ct = default);

    /// <summary>
    /// Retorna pedidos com status <c>aguardando_pagamento</c> E
    /// <c>criado_em &lt; criadoAntesDe</c> (pedidos abandonados).
    /// Limite <paramref name="maxBatch"/> evita processar lote gigante num único sweep.
    /// </summary>
    Task<IReadOnlyList<Pedido>> GetAguardandoPagamentoExpiradosAsync(
        DateTime criadoAntesDe,
        int maxBatch = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Retorna pedidos Storefront com status <c>entregue</c>,
    /// <c>entregue_em &lt;= entregueAntesDe</c> e <c>avaliacao_solicitada_em IS NULL</c>.
    /// Inclui dados do cliente (<see cref="Pedido.ClienteNome"/>, <see cref="Pedido.ClienteTelefone"/>)
    /// para envio do WhatsApp pelo handler de avaliação.
    /// </summary>
    Task<IReadOnlyList<Pedido>> GetEntreguesElegiveisPraAvaliacaoAsync(
        DateTime entregueAntesDe,
        int maxBatch = 50,
        CancellationToken ct = default);
}
