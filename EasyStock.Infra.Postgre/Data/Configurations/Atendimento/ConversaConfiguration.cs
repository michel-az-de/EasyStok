using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class ConversaConfiguration : IEntityTypeConfiguration<Conversa>
{
    public void Configure(EntityTypeBuilder<Conversa> builder)
    {
        builder.ToTable("atendimento_conversas");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.ContatoWaId).IsRequired().HasMaxLength(Conversa.ContatoWaIdTamanhoMaximo + 5);
        builder.Property(c => c.ContatoNome).HasMaxLength(Conversa.ContatoNomeTamanhoMaximo);
        builder.Property(c => c.Canal).HasConversion<int>().IsRequired();
        builder.Property(c => c.Situacao).HasConversion<int>().IsRequired();
        builder.Property(c => c.ContextoJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.IniciadaEm).IsRequired();
        builder.Property(c => c.UltimaMensagemEm).IsRequired();
        builder.Property(c => c.NaoLidas).IsRequired();

        // Invariante: uma conversa aberta por contato e empresa. Encerrada (3) sai do indice,
        // entao o mesmo contato pode abrir outra depois de encerrar (S04).
        builder.HasIndex(c => new { c.EmpresaId, c.ContatoWaId })
            .IsUnique()
            .HasFilter("\"Situacao\" <> 3")
            .HasDatabaseName("uq_atendimento_conversas_empresa_contato_aberta");

        // Listagem do console: mais recentes primeiro.
        builder.HasIndex(c => new { c.EmpresaId, c.UltimaMensagemEm })
            .IsDescending(false, true)
            .HasDatabaseName("ix_atendimento_conversas_empresa_ultima_msg");

        // Dossie do cliente: conversas recentes.
        builder.HasIndex(c => new { c.EmpresaId, c.ClienteId })
            .HasDatabaseName("ix_atendimento_conversas_empresa_cliente");

        // FK Cliente — RESTRICT: historico de atendimento sobrevive ao cadastro (LGPD anonimiza, nao apaga).
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
