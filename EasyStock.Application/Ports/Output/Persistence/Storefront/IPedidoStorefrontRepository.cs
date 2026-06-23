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

    /// <summary>
    /// Carrega o pedido com <c>SELECT FOR UPDATE</c> (lock pessimista — ADR-0014).
    /// DEVE ser chamado dentro de <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>:
    /// fora de transação explícita o lock é descartado e a corrida fica aberta.
    ///
    /// <para>
    /// Garante que dois agentes Babá não aprovem/recusem o mesmo pedido
    /// simultaneamente. O segundo aguarda commit/rollback do primeiro antes
    /// de ler o registro — o use case então detecta o status novo e devolve 409.
    /// </para>
    /// </summary>
    Task<Pedido?> GetForUpdateAsync(Guid pedidoId, CancellationToken ct = default);

    Task AddAsync(Pedido pedido, CancellationToken ct = default);

    Task UpdateAsync(Pedido pedido, CancellationToken ct = default);

    Task AddItemAsync(PedidoItem item, CancellationToken ct = default);

    /// <summary>
    /// Adiciona um <see cref="PedidoEvento"/> de audit trail (status_changed,
    /// aprovado_storefront, recusado_storefront, etc.) na MESMA transação ativa.
    /// </summary>
    Task AddEventoAsync(PedidoEvento evento, CancellationToken ct = default);

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

    /// <summary>
    /// Lista os pedidos storefront do cliente autenticado dentro do tenant
    /// (<paramref name="empresaId"/>), ordenados por <c>CriadoEm DESC</c>.
    /// Inclui <see cref="Pedido.Itens"/> via eager-load para o use case montar
    /// os DTOs sem N+1.
    ///
    /// <para>
    /// Filtros aplicados: <c>EmpresaId = @empresaId</c>, <c>ClienteId = @clienteId</c>,
    /// <c>Origem = "storefront"</c>, <c>Status != "rascunho"</c>
    /// (pedido em rascunho ainda não é visível pro cliente).
    /// </para>
    ///
    /// <para>
    /// <strong>Limit:</strong> caller responsável pelo clamp prévio (ver
    /// <c>ListarPedidosClienteUseCase</c> que aplica clamp [1, 50] default 20).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Pedido>> ListarPorClienteAsync(
        Guid empresaId,
        Guid clienteId,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Carrega UM pedido storefront do cliente autenticado, com <see cref="Pedido.Itens"/>
    /// eager-loaded. Mesmo filtro de posse do <see cref="ListarPorClienteAsync"/>:
    /// <c>Id = @pedidoId</c>, <c>EmpresaId = @empresaId</c>, <c>ClienteId = @clienteId</c>,
    /// <c>Origem = "storefront"</c>, <c>Status != "rascunho"</c>.
    ///
    /// <para>
    /// Retorna <c>null</c> se o pedido não existe OU não pertence ao cliente — o controller
    /// devolve 404 nos dois casos (anti-enumeração: não revela pedido de outro cliente).
    /// </para>
    /// </summary>
    Task<Pedido?> ObterDoClienteAsync(
        Guid empresaId,
        Guid clienteId,
        Guid pedidoId,
        CancellationToken ct = default);
}
