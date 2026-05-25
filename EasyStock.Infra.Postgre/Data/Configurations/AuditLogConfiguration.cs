using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
    {
        public void Configure(EntityTypeBuilder<AuditLog> builder)
        {
            builder.ToTable("audit_logs");

            builder.HasKey(al => al.Id);

            builder.Property(al => al.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            builder.Property(al => al.UsuarioId)
                .HasColumnType("uuid");

            builder.Property(al => al.Acao)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)");

            builder.Property(al => al.DataHora)
                .HasColumnType("timestamp with time zone");

            builder.Property(al => al.Sucesso)
                .HasColumnType("boolean");

            builder.Property(al => al.Detalhes)
                .HasMaxLength(1000)
                .HasColumnType("character varying(1000)");

            builder.Property(al => al.Ip)
                .HasMaxLength(45)
                .HasColumnType("character varying(45)");

            builder.Property(al => al.UserAgent)
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.HasOne(al => al.Usuario)
                .WithMany()
                .HasForeignKey(al => al.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(al => al.UsuarioId);
            builder.HasIndex(al => al.DataHora);
            builder.HasIndex(al => al.Acao);
        }
    }
}
