using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class PerfilPermissaoConfiguration : IEntityTypeConfiguration<PerfilPermissao>
    {
        public void Configure(EntityTypeBuilder<PerfilPermissao> builder)
        {
            builder.ToTable("perfis_permissoes");
            builder.HasKey(pp => pp.Id);
            builder.Property(pp => pp.Permissao).HasConversion<string>().IsRequired().HasMaxLength(80);
            builder.HasOne(pp => pp.Perfil).WithMany(p => p.Permissoes).HasForeignKey(pp => pp.PerfilId);
        }
    }
}
