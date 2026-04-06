using System.Threading.Tasks;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUnitOfWork
    {
        Task<int> CommitAsync();
    }
}
