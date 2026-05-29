using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class OutboxMensagemNotificacaoConfiguration : IEntityTypeConfiguration<OutboxMensagemNotificacao>
{
    public void Configure(EntityTypeBuilder<OutboxMensagemNotificacao> b)
    {
        b.ToTable("notif_outbox_mensagens");
        b.HasKey(x => x.Id);

        b.Property(x => x.EventoId).IsRequired();
        b.Property(x => x.RotinaId);
        b.Property(x => x.TemplateId).IsRequired();
        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.UsuarioDestinoId);
        b.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Destinatario).HasMaxLength(320).IsRequired();
        b.Property(x => x.AssuntoRenderizado).HasMaxLength(500);
        b.Property(x => x.CorpoRenderizado).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Tentativas).IsRequired();
        b.Property(x => x.MaxTentativas).IsRequired();
        b.Property(x => x.ProximaTentativaEm).IsRequired();
        b.Property(x => x.EnviadoEm);
        b.Property(x => x.ProviderUsado).HasMaxLength(40);
        b.Property(x => x.ErroUltimaTentativa).HasColumnType("text");
        b.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.TenantTimezone).HasMaxLength(64).IsRequired();
        b.Property(x => x.CanaisFallbackRestantesJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CriadoEm).IsRequired();
        b.Property(x => x.ShardKey).IsRequired();

        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.ProximaTentativaEm });
        b.HasIndex(x => new { x.ShardKey, x.Status, x.ProximaTentativaEm });
        b.HasIndex(x => new { x.EmpresaId, x.CriadoEm });

        b.HasOne(x => x.Evento)
            .WithMany()
            .HasForeignKey(x => x.EventoId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        b.HasOne(x => x.Rotina)
            .WithMany()
            .HasForeignKey(x => x.RotinaId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        b.HasOne(x => x.UsuarioDestino)
            .WithMany()
            .HasForeignKey(x => x.UsuarioDestinoId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
