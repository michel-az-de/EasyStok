using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class NotificacaoConfiguration : IEntityTypeConfiguration<Notificacao>
    {
        public void Configure(EntityTypeBuilder<Notificacao> builder)
        {
            builder.ToTable("notificacoes");
            builder.HasKey(n => n.Id);

            builder.Property(n => n.EmpresaId).IsRequired();
            builder.Property(n => n.TipoAlerta).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(n => n.Titulo).HasMaxLength(120).HasDefaultValue(string.Empty);
            builder.Property(n => n.Mensagem).IsRequired().HasMaxLength(500);
            builder.Property(n => n.Severidade).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SeveridadeNotificacao.Media);
            builder.Property(n => n.Lida).IsRequired();
            builder.Property(n => n.ReferenciaId);
            builder.Property(n => n.CriadaEm).IsRequired();
            builder.Property(n => n.LidaEm);

            builder.HasIndex(n => new { n.EmpresaId, n.Lida, n.CriadaEm });
            builder.HasIndex(n => new { n.EmpresaId, n.TipoAlerta, n.ReferenciaId });
            builder.HasIndex(n => new { n.EmpresaId, n.Severidade, n.Lida });

            builder.HasOne(n => n.Empresa)
                .WithMany()
                .HasForeignKey(n => n.EmpresaId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        }
    }
}
