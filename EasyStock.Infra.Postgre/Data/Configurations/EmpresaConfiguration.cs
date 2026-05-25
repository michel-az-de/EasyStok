using EasyStock.Domain.Entities;
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

            builder.Property(e => e.NomeFantasia).HasMaxLength(150);
            builder.Property(e => e.Telefone).HasMaxLength(30);
            builder.Property(e => e.Segmento).HasMaxLength(40);
            builder.Property(e => e.OnboardingCompleto).HasDefaultValue(false);
        }
    }
}
