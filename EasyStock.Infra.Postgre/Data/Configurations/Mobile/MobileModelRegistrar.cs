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

        // Onda 1 — pareamento de devices.
        // Indexes para lookup eficiente:
        //   - api_key (lookup do middleware MobileApiKey, hot path)
        //   - pairing_code (lookup do POST /devices/pair)
        //   - empresa_id (listagem no painel /dispositivos)
        mb.Entity<MobileDevice>(b =>
        {
            b.HasIndex(d => d.ApiKey).IsUnique().HasDatabaseName("ix_mobile_devices_api_key");
            b.HasIndex(d => d.PairingCode).HasDatabaseName("ix_mobile_devices_pairing_code");
            b.HasIndex(d => d.EmpresaId).HasDatabaseName("ix_mobile_devices_empresa_id");
        });

        // Onda 4 — comandos remotos enfileirados.
        mb.Entity<DeviceCommand>(b =>
        {
            b.HasIndex(c => new { c.DeviceId, c.DeliveredAt })
                .HasDatabaseName("ix_mobile_device_commands_pending");
            b.HasIndex(c => new { c.EmpresaId, c.CreatedAt })
                .HasDatabaseName("ix_mobile_device_commands_empresa");
        });

        // Onda 8 — snapshots de localStorage pra backup/restore.
        mb.Entity<DeviceBackup>(b =>
        {
            b.HasIndex(x => new { x.DeviceId, x.CreatedAt })
                .HasDatabaseName("ix_mobile_device_backups_device");
            b.HasIndex(x => new { x.EmpresaId, x.CreatedAt })
                .HasDatabaseName("ix_mobile_device_backups_empresa");
        });

        return mb;
    }
}
