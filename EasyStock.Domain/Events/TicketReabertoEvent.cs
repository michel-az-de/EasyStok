namespace EasyStock.Domain.Events
{
    /// <summary>
    /// Disparado quando ticket fechado/resolvido e reaberto pelo cliente.
    /// Reinicia o clock de SLA na Application.
    /// </summary>
    public sealed record TicketReabertoEvent(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid TicketId,
        Guid EmpresaId,
        Guid? CriadoPorId,
        string MotivoReabertura) : DomainEvent(EventoId, OcorridoEm);
}
