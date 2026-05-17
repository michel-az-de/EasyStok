using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Events
{
    /// <summary>
    /// Disparado quando o worker SLA promove automaticamente o ticket
    /// para o proximo nivel devido a violacao de prazo.
    /// </summary>
    public sealed record TicketEscalonadoAutomaticoEvent(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid TicketId,
        Guid EmpresaId,
        NivelAtendimento NivelAnterior,
        NivelAtendimento NivelNovo,
        string Motivo) : DomainEvent(EventoId, OcorridoEm);
}
