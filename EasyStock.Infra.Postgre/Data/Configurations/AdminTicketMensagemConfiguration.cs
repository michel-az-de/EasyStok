using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AdminTicketMensagemConfiguration : IEntityTypeConfiguration<AdminTicketMensagem>
    {
        public void Configure(EntityTypeBuilder<AdminTicketMensagem> builder)
        {
            builder.ToTable("admin_ticket_mensagens");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Conteudo).IsRequired().HasMaxLength(8000);
            builder.Property(m => m.LidoPeloAdmin).HasDefaultValue(false);

            builder.HasOne(m => m.Autor)
                .WithMany()
                .HasForeignKey(m => m.AutorId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
