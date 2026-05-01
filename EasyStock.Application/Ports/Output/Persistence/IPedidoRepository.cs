using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IPedidoRepository
    {
        // ── Pedido raiz ───────────────────────────────────────────
        Task<Pedido?> GetByIdAsync(Guid empresaId, Guid id);

        /// <summary>Carrega pedido com itens, eventos e pagamentos — pra tela de detalhe.</summary>
        Task<Pedido?> GetByIdWithDetailsAsync(Guid empresaId, Guid id);

        Task<Pedido?> FindByMobileOrderIdAsync(Guid empresaId, string mobileOrderId);

        Task<(IEnumerable<Pedido> items, int total)> GetByEmpresaAsync(
            Guid empresaId,
            int page,
            int pageSize,
            string? status = null,
            Guid? clienteId = null,
            DateTime? desde = null,
            DateTime? ate = null,
            string? search = null,
            string? sort = "criadoem",
            string? order = "desc");

        Task<IEnumerable<Pedido>> ListByClienteAsync(Guid empresaId, Guid clienteId, int max = 50);

        Task AddAsync(Pedido pedido);
        Task UpdateAsync(Pedido pedido);

        // ── Sub-recursos (1:N) ────────────────────────────────────
        Task AddItemAsync(PedidoItem item);
        Task RemoveItemAsync(Guid itemId);
        Task AddEventoAsync(PedidoEvento evento);
        Task<IEnumerable<PedidoEvento>> GetEventosAsync(Guid pedidoId, int max = 200);
        Task AddPagamentoAsync(PedidoPagamento pagamento);
        Task RemovePagamentoAsync(Guid pagamentoId);

        /// <summary>
        /// Verifica se existe pedido aberto (status aguardando/preparando/pronto)
        /// referenciando o produto. Usado pra bloquear inativação que orfanaria
        /// itens em produção/preparação.
        /// </summary>
        Task<bool> ExistemPedidosAbertosComProdutoAsync(Guid empresaId, Guid produtoId);
    }
}
