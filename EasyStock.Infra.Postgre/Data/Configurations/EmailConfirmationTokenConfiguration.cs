using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class EmailConfirmationTokenConfiguration : IEntityTypeConfiguration<EmailConfirmationToken>
    {
        public void Configure(EntityTypeBuilder<EmailConfirmationToken> builder)
        {
            builder.ToTable("email_confirmation_tokens");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Token)
                .IsRequired()
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.Property(e => e.CriadoEm)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.ExpiraEm)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.Confirmado)
                .IsRequired()
                .HasColumnType("boolean")
                .HasDefaultValue(false);

            builder.Property(e => e.ConfirmadoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.IpCriacao)
                .HasMaxLength(45)
                .HasColumnType("character varying(45)");

            builder.Property(e => e.UserAgent)
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(e => e.Token);
            builder.HasIndex(e => e.UsuarioId);
            builder.HasIndex(e => e.ExpiraEm);
        }
    }
}
