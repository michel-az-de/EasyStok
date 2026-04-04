using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class EmpresaRepository(EasyStockDbContext dbContext)
        : BaseRepository<Empresa>(dbContext), IEmpresaRepository
    {
    }
}
