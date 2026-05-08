using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalItemConfiguration : IEntityTypeConfiguration<NotaFiscalItem>
{
    public void Configure(EntityTypeBuilder<NotaFiscalItem> b)
    {
        b.ToTable("nota_fiscal_item");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.NotaFiscalId).HasColumnName("nota_fiscal_id").IsRequired();
        b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(x => x.Ordem).HasColumnName("ordem").IsRequired();
        b.Property(x => x.ProdutoId).HasColumnName("produto_id");

        b.Property(x => x.DescricaoSnapshot)
            .HasColumnName("descricao_snapshot")
            .HasMaxLength(120)
            .IsRequired();

        b.Property(x => x.CodigoProduto)
            .HasColumnName("codigo_produto")
            .HasMaxLength(60)
            .IsRequired();

        b.Property(x => x.Ean).HasColumnName("ean").HasMaxLength(14);

        b.Property(x => x.Ncm)
            .HasColumnName("ncm")
            .HasMaxLength(8)
            .HasConversion(v => v.Valor, s => NCM.Parse(s))
            .IsRequired();

        b.Property(x => x.Cfop)
            .HasColumnName("cfop")
            .HasMaxLength(4)
            .HasConversion(v => v.Valor, s => CFOP.Parse(s))
            .IsRequired();

        b.Property(x => x.Cest).HasColumnName("cest").HasMaxLength(7);

        b.Property(x => x.UnidadeComercial)
            .HasColumnName("unidade_comercial")
            .HasMaxLength(6)
            .IsRequired();

        b.Property(x => x.Quantidade)
            .HasColumnName("quantidade")
            .HasColumnType("numeric(15,4)")
            .IsRequired();

        b.Property(x => x.PrecoUnitario)
            .HasColumnName("preco_unitario")
            .HasColumnType("numeric(15,4)")
            .IsRequired();

        b.Property(x => x.Desconto)
            .HasColumnName("desconto")
            .HasColumnType("numeric(14,2)")
            .HasDefaultValue(0m);

        b.Property(x => x.Subtotal)
            .HasColumnName("subtotal")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d.Valor, dec => Dinheiro.FromDecimal(dec))
            .IsRequired();

        b.Property(x => x.OrigemMercadoria)
            .HasColumnName("origem_mercadoria")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.CstCsosn)
            .HasColumnName("cst_csosn")
            .HasMaxLength(4)
            .HasConversion(v => v.Valor, s => CSTouCSOSN.Parse(s))
            .IsRequired();

        b.Property(x => x.IcmsModalidadeBC).HasColumnName("icms_modalidade_bc");
        b.Property(x => x.IcmsAliquota).HasColumnName("icms_aliquota").HasColumnType("numeric(5,4)");
        b.Property(x => x.IcmsValor)
            .HasColumnName("icms_valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d == null ? (decimal?)null : d.Valor, v => v.HasValue ? Dinheiro.FromDecimal(v.Value) : null);

        b.Property(x => x.CstPis).HasColumnName("cst_pis").HasMaxLength(2).IsRequired();
        b.Property(x => x.PisAliquota).HasColumnName("pis_aliquota").HasColumnType("numeric(5,4)");
        b.Property(x => x.PisValor)
            .HasColumnName("pis_valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d == null ? (decimal?)null : d.Valor, v => v.HasValue ? Dinheiro.FromDecimal(v.Value) : null);

        b.Property(x => x.CstCofins).HasColumnName("cst_cofins").HasMaxLength(2).IsRequired();
        b.Property(x => x.CofinsAliquota).HasColumnName("cofins_aliquota").HasColumnType("numeric(5,4)");
        b.Property(x => x.CofinsValor)
            .HasColumnName("cofins_valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d == null ? (decimal?)null : d.Valor, v => v.HasValue ? Dinheiro.FromDecimal(v.Value) : null);

        b.Property(x => x.IbsCst).HasColumnName("ibs_cst").HasMaxLength(3);
        b.Property(x => x.IbsAliquota).HasColumnName("ibs_aliquota").HasColumnType("numeric(5,4)");
        b.Property(x => x.IbsValor)
            .HasColumnName("ibs_valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d == null ? (decimal?)null : d.Valor, v => v.HasValue ? Dinheiro.FromDecimal(v.Value) : null);

        b.Property(x => x.CbsCst).HasColumnName("cbs_cst").HasMaxLength(3);
        b.Property(x => x.CbsAliquota).HasColumnName("cbs_aliquota").HasColumnType("numeric(5,4)");
        b.Property(x => x.CbsValor)
            .HasColumnName("cbs_valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d == null ? (decimal?)null : d.Valor, v => v.HasValue ? Dinheiro.FromDecimal(v.Value) : null);

        b.Property(x => x.IsCst).HasColumnName("is_cst").HasMaxLength(3);
        b.Property(x => x.IsAliquota).HasColumnName("is_aliquota").HasColumnType("numeric(5,4)");
        b.Property(x => x.IsValor)
            .HasColumnName("is_valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d == null ? (decimal?)null : d.Valor, v => v.HasValue ? Dinheiro.FromDecimal(v.Value) : null);

        b.HasIndex(x => x.NotaFiscalId).HasDatabaseName("ix_nota_fiscal_item_nf");
    }
}
