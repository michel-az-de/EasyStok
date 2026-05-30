namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistência de apoio ao endpoint de auto-tickets de CI (idempotência diária).
/// A criação de tickets novos passa pelo HelpdeskTicketService; aqui ficam apenas
/// a busca do ticket aberto do dia e o anexo de comentário de reincidência (F7).
/// </summary>
public interface ICiAutoTicketRepository
{
    /// <summary>Id do ticket BugFixDev aberto hoje com o prefixo informado, ou null.</summary>
    Task<Guid?> EncontrarAbertoHojeAsync(
        Guid empresaId, string titlePrefix, DateTime hojeUtc, CancellationToken ct = default);

    /// <summary>Anexa um comentário (metadados JSON) ao ticket e toca seu AlteradoEm.</summary>
    Task AnexarReincidenciaAsync(Guid ticketId, string metadadosJson, CancellationToken ct = default);
}
