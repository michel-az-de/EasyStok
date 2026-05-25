using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class ConsentimentoNotificacaoConfiguration : IEntityTypeConfiguration<ConsentimentoNotificacao>
{
    public void Configure(EntityTypeBuilder<ConsentimentoNotificacao> b)
    {
        b.ToTable("notif_consentimentos");
        b.HasKey(x => x.Id);

        b.Property(x => x.UsuarioId).IsRequired();
        b.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.OptIn).IsRequired();
        b.Property(x => x.AtualizadoEm).IsRequired();
        b.Property(x => x.AtualizadoPor).HasMaxLength(256).IsRequired();
        b.Property(x => x.IpOrigem).HasMaxLength(64);
        b.Property(x => x.MotivoOptOut).HasMaxLength(500);

        // Versionado: histórico mantido — índice acelera "consentimento atual" (último por chave).
        b.HasIndex(x => new { x.UsuarioId, x.Canal, x.Categoria, x.AtualizadoEm });

        b.HasOne(x => x.Usuario)
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
