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

        /// <summary>
        /// Projeção plana para exportação CSV (até 10k, AsNoTracking). Replica os
        /// filtros de AdminTicketsController.GetTickets. Ids não vazio &gt; filtros.
        /// </summary>
        Task<IReadOnlyList<TicketExportRow>> ListarParaExportarAsync(
            AdminTicketExportFiltro filtro, CancellationToken ct = default);

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

    /// <summary>Filtro da exportação de tickets — espelha os filtros de GetTickets. Ids não vazio &gt; filtros.</summary>
    public sealed record AdminTicketExportFiltro(
        TicketStatus? Status = null,
        TicketPrioridade? Prioridade = null,
        NivelAtendimento? Nivel = null,
        TicketCategoria? Categoria = null,
        Guid? EmpresaId = null,
        Guid? AtendenteId = null,
        string? SlaStatus = null,
        string? Search = null,
        IReadOnlyList<Guid>? Ids = null,
        int Limite = 10000);

    /// <summary>Linha plana do CSV de tickets (nomes de Empresa/Atendente já resolvidos).</summary>
    public sealed record TicketExportRow(
        string Titulo,
        string? EmpresaNome,
        TicketCategoria Categoria,
        TicketPrioridade Prioridade,
        NivelAtendimento Nivel,
        TicketStatus Status,
        string? AtendenteNome,
        DateTime CriadoEm,
        DateTime? PrazoResposta,
        DateTime? PrazoResolucao,
        bool SlaRespostaViolado,
        bool SlaResolucaoViolado,
        DateTime? ResolvidoEm,
        int? NotaCsat);
}
