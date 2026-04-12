using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
    {
        public void Configure(EntityTypeBuilder<Usuario> builder)
        {
            builder.ToTable("usuarios");
            builder.HasKey(u => u.Id);

            builder.Property(u => u.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            builder.Property(u => u.Nome)
                .IsRequired()
                .HasMaxLength(150)
                .HasColumnType("character varying(150)");

            builder.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(255)
                .HasColumnType("character varying(255)");

            builder.Property(u => u.AvatarUrl)
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.Property(u => u.TemaPreferido)
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnType("character varying(20)")
                .HasDefaultValue("light");

            builder.Property(u => u.SenhaHash)
                .IsRequired()
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.Property(u => u.Ativo)
                .HasColumnType("boolean");

            builder.Property(u => u.UltimoAcessoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.CriadoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.AlteradoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.FailedLoginAttempts)
                .HasColumnType("integer");

            builder.Property(u => u.LockoutEnd)
                .HasColumnType("timestamp with time zone");

            builder.HasIndex(u => u.Email).IsUnique();
            builder.HasIndex(u => u.Ativo);
        }
    }
}
