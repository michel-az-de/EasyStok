using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class RotinaNotificacaoConfiguration : IEntityTypeConfiguration<RotinaNotificacao>
{
    public void Configure(EntityTypeBuilder<RotinaNotificacao> b)
    {
        b.ToTable("notif_rotinas");
        b.HasKey(x => x.Id);

        b.Property(x => x.Codigo).HasMaxLength(120).IsRequired();
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        b.Property(x => x.TipoEvento).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.TriggerTipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CronExpression).HasMaxLength(120);
        b.Property(x => x.ParametrosJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CanaisOrdemFallbackJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.TemplateCodigo).HasMaxLength(120).IsRequired();
        b.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Ativa).IsRequired();
        b.Property(x => x.EmpresaId);
        b.Property(x => x.JanelaInicio);
        b.Property(x => x.JanelaFim);
        b.Property(x => x.RespeitarFusoLoja).IsRequired();
        b.Property(x => x.CriadaEm).IsRequired();
        b.Property(x => x.AtualizadaEm).IsRequired();
        b.Property(x => x.AtualizadaPor).HasMaxLength(256).IsRequired();

        b.HasIndex(x => new { x.Codigo, x.EmpresaId }).IsUnique();
        b.HasIndex(x => new { x.Ativa, x.TipoEvento });
        b.HasIndex(x => x.EmpresaId);

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
