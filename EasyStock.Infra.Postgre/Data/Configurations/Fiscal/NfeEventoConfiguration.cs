using EasyStock.Domain.Fiscal;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public class NfeEventoConfiguration : IEntityTypeConfiguration<NfeEvento>
{
    public void Configure(EntityTypeBuilder<NfeEvento> builder)
    {
        builder.ToTable("nfe_eventos");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Tipo).IsRequired().HasMaxLength(40);
        builder.Property(e => e.DadosJson).HasColumnType("jsonb");
        builder.Property(e => e.UsuarioNome).HasMaxLength(120);
        builder.Property(e => e.Origem).HasMaxLength(20);
        builder.Property(e => e.ProtocoloEvento).HasMaxLength(50);

        builder.HasIndex(e => new { e.NfeDocumentoId, e.OcorridoEm })
            .HasDatabaseName("ix_nfe_eventos_documento_ocorrido");
    }
}
