using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AdminTicketConfiguration : IEntityTypeConfiguration<AdminTicket>
    {
        public void Configure(EntityTypeBuilder<AdminTicket> builder)
        {
            builder.ToTable("admin_tickets");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Titulo).IsRequired().HasMaxLength(200);
            builder.Property(t => t.Descricao).IsRequired().HasMaxLength(4000);
            builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
            builder.Property(t => t.Categoria).HasConversion<string>().HasMaxLength(30);
            builder.Property(t => t.Prioridade).HasConversion<string>().HasMaxLength(20);

            builder.HasOne(t => t.Empresa)
                .WithMany()
                .HasForeignKey(t => t.EmpresaId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(t => t.Atendente)
                .WithMany()
                .HasForeignKey(t => t.AtendenteId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasMany(t => t.Mensagens)
                .WithOne(m => m.Ticket)
                .HasForeignKey(m => m.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
