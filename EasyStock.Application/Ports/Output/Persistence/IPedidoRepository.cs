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
        // Remocao de item/pagamento e feita via grafo rastreado (pedido.Itens.Remove /
        // pedido.Pagamentos.Remove) nos use cases, para o DELETE participar do mesmo
        // SaveChanges do recalculo/evento (#768). Nao ha Remove*Async no port.
        Task AddItemAsync(PedidoItem item);
        Task AddEventoAsync(PedidoEvento evento);
        Task<IEnumerable<PedidoEvento>> GetEventosAsync(Guid pedidoId, int max = 200);
        Task AddPagamentoAsync(PedidoPagamento pagamento);

        /// <summary>
        /// Verifica se existe pedido aberto (status aguardando/preparando/pronto)
        /// referenciando o produto. Usado pra bloquear inativação que orfanaria
        /// itens em produção/preparação.
        /// </summary>
        Task<bool> ExistemPedidosAbertosComProdutoAsync(Guid empresaId, Guid produtoId);
    }
}
