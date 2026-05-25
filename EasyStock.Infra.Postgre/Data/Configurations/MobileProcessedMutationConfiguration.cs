using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// F10-C-3 — Configuracao da tabela <c>mobile_processed_mutations</c>.
/// PK composta (MutationId, DeviceId) garante idempotency.
/// </summary>
public class MobileProcessedMutationConfiguration : IEntityTypeConfiguration<MobileProcessedMutation>
{
    public void Configure(EntityTypeBuilder<MobileProcessedMutation> builder)
    {
        builder.ToTable("mobile_processed_mutations");

        builder.HasKey(m => new { m.MutationId, m.DeviceId });

        builder.Property(m => m.MutationId)
            .HasMaxLength(60)
            .HasColumnType("character varying(60)")
            .IsRequired();

        builder.Property(m => m.DeviceId)
            .HasMaxLength(60)
            .HasColumnType("character varying(60)")
            .IsRequired();

        builder.Property(m => m.EmpresaId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(m => m.Outcome)
            .HasMaxLength(30)
            .HasColumnType("character varying(30)")
            .IsRequired();

        builder.Property(m => m.ResponseMeta)
            .HasColumnType("text");

        builder.Property(m => m.CriadoEm)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Retention cleanup index
        builder.HasIndex(m => new { m.EmpresaId, m.CriadoEm })
            .HasDatabaseName("ix_mpm_retention");
    }
}
