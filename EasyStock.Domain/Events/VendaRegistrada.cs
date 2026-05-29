namespace EasyStock.Domain.Events
{
    public sealed record VendaRegistrada(Guid EventoId, DateTime OcorridoEm, Guid VendaId, Guid EmpresaId, decimal ValorTotal) : DomainEvent(EventoId, OcorridoEm);
}
