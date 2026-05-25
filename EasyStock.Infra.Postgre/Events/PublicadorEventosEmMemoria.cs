using EasyStock.Application.Ports.Output.Events;
using EasyStock.Domain.Events;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Events
{
    internal sealed class PublicadorEventosEmMemoria(ILogger<PublicadorEventosEmMemoria> logger)
        : IPublicadorEventos
    {
        public Task PublicarAsync<T>(T evento) where T : DomainEvent
        {
            logger.LogDebug("Evento de dominio publicado: {TipoEvento} | EventoId: {EventoId} | OcorridoEm: {OcorridoEm}",
                typeof(T).Name, evento.EventoId, evento.OcorridoEm);
            return Task.CompletedTask;
        }
    }
}
