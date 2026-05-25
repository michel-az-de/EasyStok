using System;

namespace EasyStock.Domain.Events
{
    /// <summary>
    /// Disparado quando um administrador revela dados PII de um cliente
    /// (nome, e-mail, telefone, CPF). Vai para auditoria LGPD.
    /// </summary>
    public sealed record PiiReveladaEvent(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid EmpresaId,
        Guid AdminUsuarioId,
        Guid AlvoUsuarioId,
        string Campos,
        string? Justificativa,
        Guid? TicketId) : DomainEvent(EventoId, OcorridoEm);
}
