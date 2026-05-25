using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ProdutoVariacaoConfiguration : IEntityTypeConfiguration<ProdutoVariacao>
    {
        public void Configure(EntityTypeBuilder<ProdutoVariacao> builder)
        {
            builder.ToTable("produto_variacoes");
            builder.HasKey(v => v.Id);
            builder.Property(v => v.Nome).IsRequired().HasMaxLength(120);
            builder.Property(v => v.Cor).HasMaxLength(60);
            builder.Property(v => v.Tamanho).HasMaxLength(60);
            builder.Property(v => v.CodigoBarras).HasMaxLength(100);
            builder.Property(v => v.DescricaoComercial).HasColumnType("text");
            builder.Property(v => v.AtributosJson).HasColumnType("jsonb");
            builder.Property(v => v.Sku)
                .HasConversion(
                    sku => sku == null ? null : sku.Value,
                    value => string.IsNullOrWhiteSpace(value) ? null : CodigoSku.From(value))
                .HasMaxLength(100);

            builder.OwnsOne(v => v.DimensoesPadrao, dimensions =>
            {
                dimensions.Property(d => d.Peso).HasColumnName("peso").HasColumnType("decimal(10,3)");
                dimensions.Property(d => d.Largura).HasColumnName("largura").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Altura).HasColumnName("altura").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Comprimento).HasColumnName("comprimento").HasColumnType("decimal(10,2)");
            });

            builder.HasOne(v => v.Empresa).WithMany(e => e.VariacoesProduto).HasForeignKey(v => v.EmpresaId);
            builder.HasOne(v => v.Produto).WithMany(p => p.Variacoes).HasForeignKey(v => v.ProdutoId);
            builder.HasIndex(v => new { v.ProdutoId, v.Sku }).IsUnique(false);
        }
    }
}
