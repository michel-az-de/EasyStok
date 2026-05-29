using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Infra.Postgre.Data.Configurations.Pagamentos;

public class GatewayHealthSnapshotConfiguration : IEntityTypeConfiguration<GatewayHealthSnapshot>
{
    public void Configure(EntityTypeBuilder<GatewayHealthSnapshot> b)
    {
        b.ToTable("gateway_health_snapshots");

        // PK logico = Provedor (uma linha por gateway, sem multi-tenant).
        b.HasKey(h => h.Provedor);

        b.Property(h => h.Provedor).IsRequired().HasMaxLength(40);
        b.Property(h => h.Estado).HasConversion<byte>().IsRequired();
        b.Property(h => h.UltimoErro).HasMaxLength(500);
        b.Property(h => h.AtualizadoEm).IsRequired();
    }
}
