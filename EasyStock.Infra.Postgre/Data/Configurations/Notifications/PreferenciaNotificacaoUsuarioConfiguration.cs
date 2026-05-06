using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class PreferenciaNotificacaoUsuarioConfiguration : IEntityTypeConfiguration<PreferenciaNotificacaoUsuario>
{
    public void Configure(EntityTypeBuilder<PreferenciaNotificacaoUsuario> b)
    {
        b.ToTable("notif_preferencias_usuario");
        b.HasKey(x => new { x.UsuarioId, x.RotinaCodigo });

        b.Property(x => x.UsuarioId).IsRequired();
        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.RotinaCodigo).HasMaxLength(120).IsRequired();
        b.Property(x => x.Habilitada).IsRequired();
        b.Property(x => x.CanalPreferido).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AtualizadaEm).IsRequired();

        b.HasIndex(x => new { x.EmpresaId, x.RotinaCodigo });

        b.HasOne(x => x.Usuario)
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
