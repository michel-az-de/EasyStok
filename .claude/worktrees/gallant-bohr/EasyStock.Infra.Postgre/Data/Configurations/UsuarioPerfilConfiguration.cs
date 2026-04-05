using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class UsuarioPerfilConfiguration : IEntityTypeConfiguration<UsuarioPerfil>
    {
        public void Configure(EntityTypeBuilder<UsuarioPerfil> builder)
        {
            builder.ToTable("usuarios_perfis");
            builder.HasKey(up => up.Id);
            builder.Property(up => up.LojaId).IsRequired(false);
            builder.HasOne(up => up.Usuario).WithMany(u => u.Perfis).HasForeignKey(up => up.UsuarioId);
            builder.HasOne(up => up.Perfil).WithMany(p => p.Usuarios).HasForeignKey(up => up.PerfilId);
        }
    }
}
