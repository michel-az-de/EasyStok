using EasyStock.Domain.Entities.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalEventoConfiguration : IEntityTypeConfiguration<NotaFiscalEvento>
{
    public void Configure(EntityTypeBuilder<NotaFiscalEvento> b)
    {
        b.ToTable("nota_fiscal_evento");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.NotaFiscalId).HasColumnName("nota_fiscal_id").IsRequired();
        b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(40).IsRequired();
        b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.XmlPayload).HasColumnName("xml_payload").HasColumnType("text");
        b.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        b.Property(x => x.Origem).HasColumnName("origem").HasMaxLength(20);
        b.Property(x => x.OcorridoEm).HasColumnName("ocorrido_em").IsRequired();
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);

        b.HasIndex(x => new { x.NotaFiscalId, x.OcorridoEm })
            .HasDatabaseName("ix_nota_fiscal_evento_nf");
    }
}
