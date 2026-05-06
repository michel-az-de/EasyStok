using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class TemplateNotificacaoConfiguration : IEntityTypeConfiguration<TemplateNotificacao>
{
    public void Configure(EntityTypeBuilder<TemplateNotificacao> b)
    {
        b.ToTable("notif_templates");
        b.HasKey(x => x.Id);

        b.Property(x => x.Codigo).HasMaxLength(120).IsRequired();
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.TipoEvento).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.AssuntoTemplate).HasMaxLength(500);
        b.Property(x => x.CorpoTemplate).IsRequired();
        b.Property(x => x.Idioma).HasMaxLength(10).IsRequired();
        b.Property(x => x.Ativo).IsRequired();
        b.Property(x => x.Aprovado).IsRequired();
        b.Property(x => x.EmpresaId);
        b.Property(x => x.Versao).IsRequired();
        b.Property(x => x.ChecksumSha256).HasMaxLength(64);
        b.Property(x => x.CriadoEm).IsRequired();
        b.Property(x => x.AtualizadoEm).IsRequired();
        b.Property(x => x.AtualizadoPor).HasMaxLength(256).IsRequired();

        b.HasIndex(x => new { x.Codigo, x.EmpresaId, x.Versao }).IsUnique();
        b.HasIndex(x => new { x.TipoEvento, x.Canal, x.Ativo });
        b.HasIndex(x => x.EmpresaId);

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
