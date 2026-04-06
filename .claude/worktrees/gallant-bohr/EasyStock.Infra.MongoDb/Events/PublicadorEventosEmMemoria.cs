using EasyStock.Application.Ports.Output.Events;
using EasyStock.Domain.Events;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.MongoDb.Events;

internal sealed class PublicadorEventosEmMemoria(ILogger<PublicadorEventosEmMemoria> logger) : IPublicadorEventos
{
    public Task PublicarAsync<T>(T evento) where T : DomainEvent
    {
        logger.LogDebug("Evento publicado via Mongo infra: {Evento} {EventoId}", typeof(T).Name, evento.EventoId);
        return Task.CompletedTask;
    }
}
