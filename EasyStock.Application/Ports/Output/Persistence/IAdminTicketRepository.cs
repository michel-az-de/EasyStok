namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>
    /// Acesso admin a tickets. Diferente de IClienteTicketRepository, esse
    /// expoe metodos que aceitam tickets de qualquer empresa (back-office)
    /// ou validam tenant explicitamente.
    /// </summary>
    public interface IAdminTicketRepository
    {
        Task<AdminTicket?> ObterPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default);
        Task<AdminTicket?> ObterPorIdGlobalAsync(Guid id, CancellationToken ct = default);
        Task<(IReadOnlyList<AdminTicket> Itens, int Total)> ListarAsync(
            AdminTicketFiltro filtro,
            CancellationToken ct = default);
        Task InserirAsync(AdminTicket ticket, CancellationToken ct = default);
        Task AtualizarAsync(AdminTicket ticket, CancellationToken ct = default);
    }

    public sealed record AdminTicketFiltro(
        Guid? EmpresaId,
        TicketStatus? Status,
        TicketCategoria? Categoria,
        TicketPrioridade? Prioridade,
        NivelAtendimento? Nivel,
        Guid? AtendenteId,
        bool? SlaViolado,
        int Page,
        int PageSize);
}
