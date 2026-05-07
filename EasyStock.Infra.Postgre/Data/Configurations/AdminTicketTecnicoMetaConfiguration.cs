using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AdminTicketTecnicoMetaConfiguration : IEntityTypeConfiguration<AdminTicketTecnicoMeta>
    {
        public void Configure(EntityTypeBuilder<AdminTicketTecnicoMeta> builder)
        {
            builder.ToTable("admin_ticket_tecnico_meta");
            builder.HasKey(m => m.TicketId);
            builder.Property(m => m.SeveridadeTecnica).IsRequired().HasMaxLength(20);
            builder.Property(m => m.ComponenteAfetado).HasMaxLength(120);
            builder.Property(m => m.StackTrace).HasColumnType("text");
            builder.Property(m => m.FixVersion).HasMaxLength(50);

            builder.HasOne(m => m.Ticket)
                .WithOne()
                .HasForeignKey<AdminTicketTecnicoMeta>(m => m.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
