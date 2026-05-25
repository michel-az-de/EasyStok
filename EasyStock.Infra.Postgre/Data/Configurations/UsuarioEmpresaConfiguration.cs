using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class UsuarioEmpresaConfiguration : IEntityTypeConfiguration<UsuarioEmpresa>
    {
        public void Configure(EntityTypeBuilder<UsuarioEmpresa> builder)
        {
            builder.ToTable("usuarios_empresas");
            builder.HasKey(ue => ue.Id);
            builder.HasOne(ue => ue.Usuario).WithMany(u => u.Empresas).HasForeignKey(ue => ue.UsuarioId);
            builder.HasOne(ue => ue.Empresa).WithMany().HasForeignKey(ue => ue.EmpresaId);
            builder.HasIndex(ue => new { ue.UsuarioId, ue.EmpresaId });
        }
    }
}
