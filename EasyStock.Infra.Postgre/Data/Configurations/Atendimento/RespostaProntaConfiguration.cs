using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class RespostaProntaConfiguration : IEntityTypeConfiguration<RespostaPronta>
{
    public void Configure(EntityTypeBuilder<RespostaPronta> builder)
    {
        builder.ToTable("respostas_prontas");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.EmpresaId).IsRequired();
        builder.Property(r => r.Titulo).HasMaxLength(RespostaPronta.TituloTamanhoMaximo).IsRequired();
        builder.Property(r => r.Atalho).HasMaxLength(RespostaPronta.AtalhoTamanhoMaximo).IsRequired();
        builder.Property(r => r.Texto).HasMaxLength(RespostaPronta.TextoTamanhoMaximo).IsRequired();
        builder.Property(r => r.CriadaEm).IsRequired();
        builder.Property(r => r.AlteradaEm).IsRequired();

        // S42: atalho único por empresa (a Application confere antes e devolve 409; o índice fecha a corrida).
        builder.HasIndex(r => new { r.EmpresaId, r.Atalho })
            .IsUnique()
            .HasDatabaseName("ux_respostas_prontas_empresa_atalho");
    }
}
