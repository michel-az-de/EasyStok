using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class TicketAnexoConfiguration : IEntityTypeConfiguration<TicketAnexo>
    {
        public void Configure(EntityTypeBuilder<TicketAnexo> builder)
        {
            builder.ToTable("ticket_anexos");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.NomeArquivo).IsRequired().HasMaxLength(255);
            builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(500);
            builder.Property(a => a.Url).IsRequired().HasMaxLength(1000);
            builder.Property(a => a.TamanhoBytes).IsRequired();
            builder.Property(a => a.IsPublico).HasDefaultValue(false);
            builder.Property(a => a.IsAdmin).HasDefaultValue(false);

            builder.HasOne(a => a.Ticket)
                .WithMany()
                .HasForeignKey(a => a.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(a => a.Mensagem)
                .WithMany()
                .HasForeignKey(a => a.MensagemId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(a => a.EnviadoPor)
                .WithMany()
                .HasForeignKey(a => a.EnviadoPorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(a => a.TicketId).HasDatabaseName("ix_ticket_anexos_ticket_id");
            builder.HasIndex(a => a.MensagemId).HasDatabaseName("ix_ticket_anexos_mensagem_id");
        }
    }
}
