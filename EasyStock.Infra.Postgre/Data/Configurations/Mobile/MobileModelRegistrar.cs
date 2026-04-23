using EasyStock.Domain.Entities.Mobile;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Data.Configurations.Mobile;

/// <summary>
/// Registra as entidades do módulo Casa da Baba Mobile no <see cref="ModelBuilder"/>.
/// Invocado pelo <c>EasyStockDbContext.OnModelCreating</c>.
///
/// As tabelas/colunas já estão mapeadas via data annotations (<c>[Table]</c>,
/// <c>[Column]</c>) para bater com o schema SQL do arquivo
/// <c>001_CreateMobileSchema.sql</c> (snake_case, prefixo <c>mobile_</c>).
/// Aqui só amarramos relações 1:N com cascade.
/// </summary>
public static class MobileModelRegistrar
{
    public static ModelBuilder RegisterMobileModels(this ModelBuilder mb)
    {
        mb.Entity<Product>();
        mb.Entity<Client>();

        mb.Entity<Order>()
            .HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<OrderItem>();

        mb.Entity<Batch>()
            .HasMany(b => b.Items)
            .WithOne(i => i.Batch)
            .HasForeignKey(i => i.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<BatchItem>();

        mb.Entity<CashEntry>();

        return mb;
    }
}
