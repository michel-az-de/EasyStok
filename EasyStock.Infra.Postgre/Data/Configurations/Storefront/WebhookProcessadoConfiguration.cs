using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class WebhookProcessadoConfiguration : IEntityTypeConfiguration<WebhookProcessado>
{
    public void Configure(EntityTypeBuilder<WebhookProcessado> builder)
    {
        builder.ToTable("webhook_processado");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Provider)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(w => w.EventoId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(w => w.Tipo)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(w => w.PayloadRaw)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(w => w.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(w => w.Motivo)
            .HasMaxLength(500);

        builder.Property(w => w.RecebidoEm).IsRequired();
        builder.Property(w => w.ProcessadoEm);
        builder.Property(w => w.EmpresaId);

        // Dedup: (Provider, EventoId) único — segundo POST do mesmo evento falha com unique violation
        builder.HasIndex(w => new { w.Provider, w.EventoId })
            .IsUnique()
            .HasDatabaseName("uq_webhook_processado_provider_evento");

        // Lookup do job de processamento: pega só pendentes, ordenando por chegada.
        // Índice filtrado (status = 0 = Received) mantém só o backlog ativo, evitando bloat.
        builder.HasIndex(w => new { w.Status, w.RecebidoEm })
            .HasFilter("\"status\" = 0")
            .HasDatabaseName("ix_webhook_processado_received_recebido_em");
    }
}
