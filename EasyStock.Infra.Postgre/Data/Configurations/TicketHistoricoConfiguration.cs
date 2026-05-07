using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class TicketHistoricoConfiguration : IEntityTypeConfiguration<TicketHistorico>
    {
        public void Configure(EntityTypeBuilder<TicketHistorico> builder)
        {
            builder.ToTable("ticket_historico");
            builder.HasKey(h => h.Id);
            builder.Property(h => h.Acao).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(h => h.ValorAntes).HasMaxLength(500);
            builder.Property(h => h.ValorDepois).HasMaxLength(500);
            builder.Property(h => h.MetadadosJson).HasColumnType("jsonb");

            builder.HasOne(h => h.Ticket)
                .WithMany()
                .HasForeignKey(h => h.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(h => h.Autor)
                .WithMany()
                .HasForeignKey(h => h.AutorId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(h => new { h.TicketId, h.CriadoEm }).HasDatabaseName("ix_ticket_historico_ticket_criado");
        }
    }
}
