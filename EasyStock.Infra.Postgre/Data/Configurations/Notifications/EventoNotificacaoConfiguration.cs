using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class EventoNotificacaoConfiguration : IEntityTypeConfiguration<EventoNotificacao>
{
    public void Configure(EntityTypeBuilder<EventoNotificacao> b)
    {
        b.ToTable("notif_eventos");
        b.HasKey(x => x.Id);

        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.RefEntidadeId);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OcorridoEm).IsRequired();
        b.Property(x => x.ProcessadoEm);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.ErroProcessamento).HasMaxLength(2000);

        b.HasIndex(x => new { x.Status, x.OcorridoEm });
        b.HasIndex(x => new { x.EmpresaId, x.Tipo });

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
