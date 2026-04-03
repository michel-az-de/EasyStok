using EasyStok.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
 public class CategoriaConfiguration : IEntityTypeConfiguration<Categoria>
 {
 public void Configure(EntityTypeBuilder<Categoria> builder)
 {
 builder.ToTable("categorias");
 builder.HasKey(c => c.Id);
 builder.Property(c => c.Nome).IsRequired().HasMaxLength(120);
 builder.HasOne(c => c.Empresa).WithMany(e => e.Categorias).HasForeignKey(c => c.EmpresaId);
 builder.HasOne(c => c.CategoriaPai).WithMany(c => c.SubCategorias).HasForeignKey(c => c.CategoriaPaiId).IsRequired(false);
 }
 }
}
