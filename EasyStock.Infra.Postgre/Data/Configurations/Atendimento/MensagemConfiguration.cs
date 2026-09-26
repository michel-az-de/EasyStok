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
        builder.Property(m => m.MidiaChave).HasMaxLength(Mensagem.MidiaChaveTamanhoMaximo);
        builder.Property(m => m.MidiaMime).HasMaxLength(Mensagem.MidiaMimeTamanhoMaximo);
        builder.Property(m => m.Status).HasConversion<int>().IsRequired();
        builder.Property(m => m.Erro).HasMaxLength(Mensagem.ErroTamanhoMaximo);
        builder.Property(m => m.EnviadaEm).IsRequired();

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
