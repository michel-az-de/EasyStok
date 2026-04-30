using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> b)
    {
        b.ToTable("admin_audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.AdminEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.Acao).HasMaxLength(100).IsRequired();
        b.Property(x => x.Detalhes).HasMaxLength(2000);
        b.Property(x => x.Ip).HasMaxLength(64);
        b.HasIndex(x => x.CriadoEm);
        b.HasIndex(x => x.TenantId);
    }
}
