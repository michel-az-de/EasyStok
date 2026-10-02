using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class MensagemConfiguration : IEntityTypeConfiguration<Mensagem>
{
    public void Configure(EntityTypeBuilder<Mensagem> builder)
    {
        builder.ToTable("atendimento_mensagens");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.EmpresaId).IsRequired();
        builder.Property(m => m.ConversaId).IsRequired();
        builder.Property(m => m.ExternoId).HasMaxLength(Mensagem.ExternoIdTamanhoMaximo);
        builder.Property(m => m.Direcao).HasConversion<int>().IsRequired();
        builder.Property(m => m.Autor).HasConversion<int>().IsRequired();
        builder.Property(m => m.TipoConteudo).HasConversion<int>().IsRequired();
        builder.Property(m => m.Texto).HasMaxLength(Mensagem.TextoTamanhoMaximo);
        builder.Property(m => m.BotaoId).HasMaxLength(Mensagem.BotaoIdTamanhoMaximo);
        builder.Property(m => m.Programada).HasDefaultValue(false);
        builder.Property(m => m.MidiaChave).HasMaxLength(Mensagem.MidiaChaveTamanhoMaximo);
        builder.Property(m => m.MidiaMime).HasMaxLength(Mensagem.MidiaMimeTamanhoMaximo);
        builder.Property(m => m.Status).HasConversion<int>().IsRequired();
        builder.Property(m => m.Erro).HasMaxLength(Mensagem.ErroTamanhoMaximo);
        builder.Property(m => m.EnviadaEm).IsRequired();
        builder.Property(m => m.TentativasEnvio).HasDefaultValue(0);
        builder.Property(m => m.UltimaFalhaEnvio).HasConversion<int?>();

        // S58: o serviço de reenvio procura quem espera o cliente responder ao modelo de retomada.
        builder.HasIndex(m => m.AguardaClienteDesde)
            .HasFilter("\"AguardaClienteDesde\" IS NOT NULL")
            .HasDatabaseName("ix_atendimento_mensagens_aguarda_cliente");

        // S59: painel "Não entregues" (saída que falhou, por empresa, mais recente primeiro).
        builder.HasIndex(m => new { m.EmpresaId, m.EnviadaEm })
            .HasFilter("\"Status\" = 5 AND \"Direcao\" = 2")
            .HasDatabaseName("ix_atendimento_mensagens_nao_entregues");

        // S57: o serviço de reenvio só procura mensagens com reenvio agendado.
        builder.HasIndex(m => m.ProximoReenvioEm)
            .HasFilter("\"ProximoReenvioEm\" IS NOT NULL")
            .HasDatabaseName("ix_atendimento_mensagens_proximo_reenvio");

        // Idempotencia do webhook: o mesmo wamid nao entra duas vezes na mesma empresa.
        builder.HasIndex(m => new { m.EmpresaId, m.ExternoId })
            .IsUnique()
            .HasFilter("\"ExternoId\" IS NOT NULL")
            .HasDatabaseName("uq_atendimento_mensagens_empresa_externo");

        // Thread da conversa em ordem cronologica.
        builder.HasIndex(m => new { m.ConversaId, m.EnviadaEm })
            .HasDatabaseName("ix_atendimento_mensagens_conversa_enviada");

        // FK Conversa — RESTRICT: mensagem nunca fica orfa nem some junto com a conversa.
        builder.HasOne<Conversa>()
            .WithMany()
            .HasForeignKey(m => m.ConversaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
