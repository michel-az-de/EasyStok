using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class AdminNotaTenantConfiguration : IEntityTypeConfiguration<AdminNotaTenant>
{
    public void Configure(EntityTypeBuilder<AdminNotaTenant> b)
    {
        b.ToTable("admin_notas_tenant");
        b.HasKey(x => x.Id);
        b.Property(x => x.AutorEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.Texto).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Tipo).HasConversion<int>().IsRequired();
        // Tab Notas faz query "todas notas ativas deste tenant ordenadas desc" —
        // index composto em TenantId+CriadoEm cobre direto sem sort em memória.
        b.HasIndex(x => new { x.TenantId, x.CriadoEm });
    }
}
