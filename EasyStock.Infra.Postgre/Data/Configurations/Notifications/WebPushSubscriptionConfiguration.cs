using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

/// <summary>Onda 2.2 — EF config para subscriptions de Web Push.</summary>
public class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> b)
    {
        b.ToTable("notif_web_push_subscriptions");
        b.HasKey(x => x.Id);

        b.Property(x => x.EmpresaId);
        b.Property(x => x.UsuarioId);
        // Endpoint do push service eh longo (URLs com tokens base64 podem ter 500+ chars).
        b.Property(x => x.Endpoint).HasMaxLength(2000).IsRequired();
        b.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
        b.Property(x => x.Auth).HasMaxLength(200).IsRequired();
        b.Property(x => x.UserAgent).HasMaxLength(500);
        b.Property(x => x.CriadoEm).IsRequired();
        b.Property(x => x.UltimoUso).IsRequired();
        b.Property(x => x.Ativo).IsRequired().HasDefaultValue(true);

        // Endpoint identifica unicamente uma subscription do browser. Unique para
        // idempotencia do POST /subscribe (re-registro mantem id existente).
        b.HasIndex(x => x.Endpoint).IsUnique().HasDatabaseName("ux_web_push_endpoint");
        b.HasIndex(x => new { x.UsuarioId, x.Ativo }).HasDatabaseName("ix_web_push_usuario_ativo");
        b.HasIndex(x => new { x.EmpresaId, x.Ativo }).HasDatabaseName("ix_web_push_empresa_ativo");

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Usuario)
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
