using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class WebhookRecebidoConfiguration : IEntityTypeConfiguration<WebhookRecebido>
{
    public void Configure(EntityTypeBuilder<WebhookRecebido> builder)
    {
        builder.ToTable("webhook_recebidos");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Provedor).IsRequired().HasMaxLength(40);
        builder.Property(w => w.EventIdExterno).IsRequired().HasMaxLength(200);
        builder.Property(w => w.RawBodyHash).IsRequired().HasMaxLength(64);
        builder.Property(w => w.Erro).HasMaxLength(2000);

        builder.HasIndex(w => new { w.Provedor, w.EventIdExterno })
            .IsUnique()
            .HasDatabaseName("ux_webhook_recebidos_provedor_eventid");
        builder.HasIndex(w => w.RecebidoEm)
            .HasDatabaseName("ix_webhook_recebidos_recebido_em");
    }
}
