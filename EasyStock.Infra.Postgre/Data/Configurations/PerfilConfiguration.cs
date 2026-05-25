using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class PerfilConfiguration : IEntityTypeConfiguration<Perfil>
    {
        public void Configure(EntityTypeBuilder<Perfil> builder)
        {
            builder.ToTable("perfis");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Nome).IsRequired().HasMaxLength(80);
            builder.Property(p => p.Descricao).HasMaxLength(500);
            builder.Property(p => p.Nivel).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.HasOne(p => p.Empresa).WithMany().HasForeignKey(p => p.EmpresaId).IsRequired(false);
            builder.HasIndex(p => p.EmpresaId);
            builder.HasIndex(p => new { p.EmpresaId, p.Nome }).IsUnique();
        }
    }
}
