using System;

namespace EasyStock.Domain.Events
{
    public abstract record DomainEvent(Guid EventoId, DateTime OcorridoEm);
}
