using EasyStock.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class LogEnvioNotificacaoConfiguration : IEntityTypeConfiguration<LogEnvioNotificacao>
{
    public void Configure(EntityTypeBuilder<LogEnvioNotificacao> b)
    {
        b.ToTable("notif_logs_envio");
        b.HasKey(x => x.Id);

        b.Property(x => x.OutboxMensagemId).IsRequired();
        b.Property(x => x.Tentativa).IsRequired();
        b.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.StatusHttp);
        b.Property(x => x.RespostaProviderJson).HasColumnType("jsonb");
        b.Property(x => x.DuracaoMs).IsRequired();
        b.Property(x => x.OcorridoEm).IsRequired();
        b.Property(x => x.ErroDetalhado).HasColumnType("text");
        b.Property(x => x.BypassConsentimento).IsRequired();
        b.Property(x => x.Sucesso).IsRequired();

        b.HasIndex(x => new { x.OutboxMensagemId, x.Tentativa });
        b.HasIndex(x => x.OcorridoEm);

        b.HasOne(x => x.OutboxMensagem)
            .WithMany()
            .HasForeignKey(x => x.OutboxMensagemId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
