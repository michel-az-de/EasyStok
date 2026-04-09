using EasyStock.Domain.Events;

namespace EasyStock.Application.Ports.Output.Events
{
    public interface IPublicadorEventos
    {
        Task PublicarAsync<T>(T evento) where T : DomainEvent;
    }
}
