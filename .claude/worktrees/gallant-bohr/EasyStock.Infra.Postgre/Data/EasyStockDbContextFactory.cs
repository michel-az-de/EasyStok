using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EasyStock.Infra.Postgre.Data
{
    public class EasyStockDbContextFactory : IDesignTimeDbContextFactory<EasyStockDbContext>
    {
        public EasyStockDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=EasyStockDb;Username=postgres;Password=postgres")
                .Options;
            return new EasyStockDbContext(options);
        }
    }
}
