using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class BloqueioNotificacaoConfiguration : IEntityTypeConfiguration<BloqueioNotificacao>
{
    public void Configure(EntityTypeBuilder<BloqueioNotificacao> b)
    {
        b.ToTable("notif_bloqueios");
        b.HasKey(x => x.Id);

        b.Property(x => x.EmpresaId);
        b.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Motivo).HasMaxLength(500).IsRequired();
        b.Property(x => x.AtivadoEm).IsRequired();
        b.Property(x => x.AtivadoPor).HasMaxLength(256).IsRequired();
        b.Property(x => x.ExpiraEm);
        b.Property(x => x.RemovidoEm);
        b.Property(x => x.RemovidoPor).HasMaxLength(256);

        b.HasIndex(x => new { x.EmpresaId, x.Canal, x.RemovidoEm });

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
