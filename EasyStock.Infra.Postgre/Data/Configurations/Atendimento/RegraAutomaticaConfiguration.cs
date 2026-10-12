using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class RegraAutomaticaConfiguration : IEntityTypeConfiguration<RegraAutomatica>
{
    public void Configure(EntityTypeBuilder<RegraAutomatica> builder)
    {
        builder.ToTable("regras_automaticas");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.EmpresaId).IsRequired();
        builder.Property(r => r.Gatilho).HasConversion<int>().IsRequired();
        builder.Property(r => r.Texto).HasMaxLength(RegraAutomatica.TextoTamanhoMaximo).IsRequired();
        builder.Property(r => r.CriadaEm).IsRequired();
        builder.Property(r => r.AlteradaEm).IsRequired();

        // S42: uma regra por gatilho e empresa.
        builder.HasIndex(r => new { r.EmpresaId, r.Gatilho })
            .IsUnique()
            .HasDatabaseName("ux_regras_automaticas_empresa_gatilho");
    }
}
