using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.ToTable("worker_heartbeats");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Servico).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Detalhe).HasMaxLength(500);

        builder.Property(x => x.UltimoTickEm);
        builder.Property(x => x.CriadoEm);
        builder.Property(x => x.AlteradoEm);

        // Servico eh unique — 1 linha por job, UPSERT a cada tick.
        builder.HasIndex(x => x.Servico)
            .IsUnique()
            .HasDatabaseName("ux_worker_heartbeats_servico");

        // Index pra ordenar por atividade recente no dashboard.
        builder.HasIndex(x => x.UltimoTickEm)
            .HasDatabaseName("ix_worker_heartbeats_ultimo_tick");
    }
}
