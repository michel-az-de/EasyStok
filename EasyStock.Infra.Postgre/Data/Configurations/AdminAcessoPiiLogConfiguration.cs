using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class AdminAcessoPiiLogConfiguration : IEntityTypeConfiguration<AdminAcessoPiiLog>
{
    public void Configure(EntityTypeBuilder<AdminAcessoPiiLog> b)
    {
        b.ToTable("admin_acessos_pii_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.AdminEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.EntidadeTipo).HasMaxLength(40).IsRequired();
        b.Property(x => x.Campo).HasMaxLength(40).IsRequired();
        b.Property(x => x.Motivo).HasMaxLength(1000);
        b.Property(x => x.Ip).HasMaxLength(64);
        // Relatório ANPD comum: "todas as visualizações de PII por X operador nos últimos 90 dias".
        b.HasIndex(x => x.AdminEmail);
        b.HasIndex(x => x.CriadoEm);
        b.HasIndex(x => new { x.TenantId, x.EntidadeId });
        // Relatório por tenant: "todos os acessos PII na minha empresa nos últimos N dias".
        b.HasIndex(x => new { x.TenantId, x.CriadoEm })
            .HasDatabaseName("ix_admin_acessos_pii_logs_tenant_criado");
    }
}
