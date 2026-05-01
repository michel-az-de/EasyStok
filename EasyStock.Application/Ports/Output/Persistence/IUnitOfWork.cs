using System.Threading.Tasks;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUnitOfWork
    {
        Task<int> CommitAsync();

        /// <summary>
        /// Inicia transação explícita. Use quando precisar manter locks
        /// pessimistas (FOR UPDATE) entre múltiplas operações até o commit.
        /// Sem isso, EF abre transação implícita só no SaveChanges, e locks
        /// adquiridos antes (ex: GetByIdComLockAsync) já foram liberados.
        /// </summary>
        Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default);
    }
}
