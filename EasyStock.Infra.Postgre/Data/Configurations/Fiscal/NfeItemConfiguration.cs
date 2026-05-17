using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public class NfeItemConfiguration : IEntityTypeConfiguration<NfeItem>
{
    public void Configure(EntityTypeBuilder<NfeItem> builder)
    {
        builder.ToTable("nfe_itens");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.NomeSnapshot).IsRequired().HasMaxLength(300);
        builder.Property(i => i.NcmSnapshot).HasMaxLength(8);
        builder.Property(i => i.CfopSnapshot).HasMaxLength(4);
        builder.Property(i => i.Unidade).IsRequired().HasMaxLength(6);
        builder.Property(i => i.CstOuCsosn).HasMaxLength(4);

        // Tributos por linha — nullable (NULL = NFC-e legada anterior ao PR-D)
        builder.Property(i => i.BaseIcms).HasColumnType("numeric(14,2)");
        builder.Property(i => i.ValorIcms).HasColumnType("numeric(14,2)");
        builder.Property(i => i.Pis).HasColumnType("numeric(14,2)");
        builder.Property(i => i.Cofins).HasColumnType("numeric(14,2)");

        builder.Property(i => i.Quantidade).HasColumnType("numeric(14,3)");

        builder.Property(i => i.PrecoUnitario)
            .HasConversion(
                v => v.Valor,
                v => Dinheiro.FromDecimal(v))
            .HasColumnType("numeric(14,2)")
            .IsRequired();

        builder.Property(i => i.Subtotal)
            .HasConversion(
                v => v.Valor,
                v => Dinheiro.FromDecimal(v))
            .HasColumnType("numeric(14,2)")
            .IsRequired();

        builder.HasIndex(i => new { i.NfeDocumentoId, i.Ordem })
            .HasDatabaseName("ix_nfe_itens_documento_ordem");
    }
}
