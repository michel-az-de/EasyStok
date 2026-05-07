using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class SlaConfiguracaoConfiguration : IEntityTypeConfiguration<SlaConfiguracao>
    {
        public void Configure(EntityTypeBuilder<SlaConfiguracao> builder)
        {
            builder.ToTable("sla_configuracao");
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Prioridade).HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(s => s.MinutosResposta).IsRequired();
            builder.Property(s => s.MinutosResolucao).IsRequired();
            builder.Property(s => s.HorarioComercialApenas).HasDefaultValue(false);

            builder.HasOne(s => s.Empresa)
                .WithMany()
                .HasForeignKey(s => s.EmpresaId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(s => s.Plano)
                .WithMany()
                .HasForeignKey(s => s.PlanoId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(s => new { s.EmpresaId, s.Prioridade })
                .HasDatabaseName("ix_sla_configuracao_empresa_prioridade")
                .HasFilter("\"EmpresaId\" IS NOT NULL");
            builder.HasIndex(s => new { s.PlanoId, s.Prioridade })
                .HasDatabaseName("ix_sla_configuracao_plano_prioridade")
                .HasFilter("\"PlanoId\" IS NOT NULL");
        }
    }
}
