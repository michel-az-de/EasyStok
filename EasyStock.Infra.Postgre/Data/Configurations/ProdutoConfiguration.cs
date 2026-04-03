using EasyStok.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
 public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
 {
 public void Configure(EntityTypeBuilder<Produto> builder)
 {
 builder.ToTable("produtos");
 builder.HasKey(p => p.Id);
 builder.Property(p => p.Nome).IsRequired().HasMaxLength(180);
 builder.Property(p => p.Marca).HasMaxLength(120);
 builder.Property(p => p.Tipo).IsRequired().HasMaxLength(50);
 builder.Property(p => p.SkuBase).HasMaxLength(100);
 builder.Property(p => p.CodigoBarras).HasMaxLength(100);

 builder.Property(p => p.Peso).HasColumnType("decimal(10,3)");
 builder.Property(p => p.Largura).HasColumnType("decimal(10,2)");
 builder.Property(p => p.Altura).HasColumnType("decimal(10,2)");
 builder.Property(p => p.Comprimento).HasColumnType("decimal(10,2)");

 builder.Property(p => p.CustoReferencia).HasColumnType("decimal(18,2)");
 builder.Property(p => p.PrecoReferencia).HasColumnType("decimal(18,2)");
 builder.Property(p => p.MargemEstimada).HasColumnType("decimal(8,2)");

 builder.Property(p => p.AtributosJson).HasColumnType("jsonb");
 builder.Property(p => p.FotosJson).HasColumnType("jsonb");
 builder.Property(p => p.EmbalagemJson).HasColumnType("jsonb");

 builder.HasOne(p => p.Empresa).WithMany(e => e.Produtos).HasForeignKey(p => p.EmpresaId);
 builder.HasOne(p => p.Categoria).WithMany(c => c.Produtos).HasForeignKey(p => p.CategoriaId);
 }
 }
}
