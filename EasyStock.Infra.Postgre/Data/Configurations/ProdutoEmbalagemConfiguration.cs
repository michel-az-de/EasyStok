using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ProdutoEmbalagemConfiguration : IEntityTypeConfiguration<ProdutoEmbalagem>
    {
        public void Configure(EntityTypeBuilder<ProdutoEmbalagem> builder)
        {
            builder.ToTable("produto_embalagens");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Nome).IsRequired().HasMaxLength(120);
            builder.Property(e => e.Descricao).HasColumnType("text");

            builder.OwnsOne(e => e.Dimensoes, dimensions =>
            {
                dimensions.Property(d => d.Peso).HasColumnName("peso").HasColumnType("decimal(10,3)");
                dimensions.Property(d => d.Largura).HasColumnName("largura").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Altura).HasColumnName("altura").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Comprimento).HasColumnName("comprimento").HasColumnType("decimal(10,2)");
            });

            builder.HasOne(e => e.Empresa).WithMany(emp => emp.EmbalagensProduto).HasForeignKey(e => e.EmpresaId);
            builder.HasOne(e => e.Produto).WithMany(p => p.Embalagens).HasForeignKey(e => e.ProdutoId);
        }
    }
}
