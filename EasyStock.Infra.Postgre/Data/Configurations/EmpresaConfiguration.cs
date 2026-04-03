using EasyStok.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
 public class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
 {
 public void Configure(EntityTypeBuilder<Empresa> builder)
 {
 builder.ToTable("empresas");
 builder.HasKey(e => e.Id);
 builder.Property(e => e.Nome).IsRequired().HasMaxLength(150);
 builder.Property(e => e.Documento).HasMaxLength(30);
 builder.HasIndex(e => e.Documento).IsUnique();
 }
 }
}
