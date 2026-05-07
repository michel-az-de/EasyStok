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
            builder.Property(t => t.Nivel).HasConversion<string>().HasMaxLength(4).HasDefaultValue(Domain.Enums.NivelAtendimento.N1);
            builder.Property(t => t.SlaRespostaViolado).HasDefaultValue(false);
            builder.Property(t => t.SlaResolucaoViolado).HasDefaultValue(false);

            builder.HasOne(t => t.Empresa)
                .WithMany()
                .HasForeignKey(t => t.EmpresaId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(t => t.Atendente)
                .WithMany()
                .HasForeignKey(t => t.AtendenteId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(t => t.OrigemTicket)
                .WithMany()
                .HasForeignKey(t => t.OrigemTicketId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasMany(t => t.Mensagens)
                .WithOne(m => m.Ticket)
                .HasForeignKey(m => m.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(t => new { t.EmpresaId, t.Status, t.Prioridade })
                .HasDatabaseName("ix_admin_tickets_empresa_status_prioridade");
            builder.HasIndex(t => new { t.AtendenteId, t.Status })
                .HasDatabaseName("ix_admin_tickets_atendente_status");
            builder.HasIndex(t => new { t.Nivel, t.Status })
                .HasDatabaseName("ix_admin_tickets_nivel_status");
            builder.HasIndex(t => t.OrigemTicketId)
                .HasDatabaseName("ix_admin_tickets_origem_ticket_id");
            builder.HasIndex(t => new { t.Status, t.PrazoResposta })
                .HasDatabaseName("ix_admin_tickets_status_prazo_resposta")
                .HasFilter("\"Status\" IN ('Aberto','EmAtendimento','AguardandoCliente') AND \"PrazoResposta\" IS NOT NULL");
            builder.HasIndex(t => new { t.Status, t.PrazoResolucao })
                .HasDatabaseName("ix_admin_tickets_status_prazo_resolucao")
                .HasFilter("\"Status\" IN ('Aberto','EmAtendimento','AguardandoCliente') AND \"PrazoResolucao\" IS NOT NULL");
        }
    }
}
