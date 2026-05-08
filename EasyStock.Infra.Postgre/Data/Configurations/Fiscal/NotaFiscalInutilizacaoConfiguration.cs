using EasyStock.Domain.Entities.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalInutilizacaoConfiguration : IEntityTypeConfiguration<NotaFiscalInutilizacao>
{
    public void Configure(EntityTypeBuilder<NotaFiscalInutilizacao> b)
    {
        b.ToTable("nota_fiscal_inutilizacao");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(x => x.LojaId).HasColumnName("loja_id").IsRequired();
        b.Property(x => x.Modelo).HasColumnName("modelo").HasConversion<short>().IsRequired();
        b.Property(x => x.Serie).HasColumnName("serie").IsRequired();
        b.Property(x => x.NumeroInicial).HasColumnName("numero_inicial").IsRequired();
        b.Property(x => x.NumeroFinal).HasColumnName("numero_final").IsRequired();
        b.Property(x => x.Ano).HasColumnName("ano").IsRequired();
        b.Property(x => x.Justificativa).HasColumnName("justificativa").HasColumnType("text").IsRequired();
        b.Property(x => x.ProtocoloInutilizacao).HasColumnName("protocolo_inutilizacao").HasMaxLength(15);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasConversion<string>().IsRequired();
        b.Property(x => x.XmlInutilizacao).HasColumnName("xml_inutilizacao").HasColumnType("text");
        b.Property(x => x.MotivoRejeicao).HasColumnName("motivo_rejeicao").HasColumnType("text");
        b.Property(x => x.CriadoEm).HasColumnName("created_at").IsRequired();
        b.Property(x => x.AlteradoEm).HasColumnName("updated_at").IsRequired();

        b.HasIndex(x => new { x.EmpresaId, x.LojaId, x.Modelo, x.Serie, x.Ano })
            .HasDatabaseName("ix_nota_fiscal_inut_loja_serie_ano");
    }
}
