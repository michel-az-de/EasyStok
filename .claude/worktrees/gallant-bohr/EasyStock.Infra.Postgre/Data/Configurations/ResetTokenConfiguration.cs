using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ResetTokenConfiguration : IEntityTypeConfiguration<ResetToken>
    {
        public void Configure(EntityTypeBuilder<ResetToken> builder)
        {
            builder.ToTable("reset_tokens");

            builder.HasKey(rt => rt.Id);

            builder.Property(rt => rt.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            builder.Property(rt => rt.UsuarioId)
                .HasColumnType("uuid");

            builder.Property(rt => rt.Token)
                .IsRequired()
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.Property(rt => rt.CriadoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(rt => rt.ExpiraEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(rt => rt.Usado)
                .HasColumnType("boolean");

            builder.Property(rt => rt.IpCriacao)
                .HasMaxLength(45)
                .HasColumnType("character varying(45)");

            builder.Property(rt => rt.UserAgent)
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.HasOne(rt => rt.Usuario)
                .WithMany()
                .HasForeignKey(rt => rt.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(rt => rt.Token);
            builder.HasIndex(rt => rt.UsuarioId);
            builder.HasIndex(rt => rt.ExpiraEm);
        }
    }
}