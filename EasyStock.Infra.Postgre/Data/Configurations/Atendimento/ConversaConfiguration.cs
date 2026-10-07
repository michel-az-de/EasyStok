using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class ConversaConfiguration : IEntityTypeConfiguration<Conversa>
{
    public void Configure(EntityTypeBuilder<Conversa> builder)
    {
        builder.ToTable("atendimento_conversas");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.ContatoIdExterno).IsRequired().HasMaxLength(Conversa.ContatoIdExternoTamanhoMaximo);
        builder.Property(c => c.ContatoNome).HasMaxLength(Conversa.ContatoNomeTamanhoMaximo);
        // Contato informado pelo visitante do chat do site (#1430), até a loja confirmar o cadastro.
        builder.Ignore(c => c.ContatoInformado);
        builder.Property(c => c.ContatoTelefoneInformado).HasMaxLength(TelefoneE164.TamanhoMaximo);
        builder.Property(c => c.ContatoEmailInformado).HasMaxLength(ContatoInformadoVisitante.EmailTamanhoMaximo);
        builder.Property(c => c.MotivoEscalada).HasMaxLength(Conversa.MotivoEscaladaTamanhoMaximo);
        builder.Property(c => c.Canal).HasConversion<int>().IsRequired();
        builder.Property(c => c.Situacao).HasConversion<int>().IsRequired();
        builder.Property(c => c.ContextoJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.IniciadaEm).IsRequired();
        builder.Property(c => c.UltimaMensagemEm).IsRequired();
        builder.Property(c => c.NaoLidas).IsRequired();

        // Invariante: uma conversa aberta por canal, contato e empresa. Encerrada (3) sai do indice,
        // entao o mesmo contato pode abrir outra depois de encerrar (S04). O mesmo id em canais
        // diferentes sao contatos diferentes (S34).
        builder.HasIndex(c => new { c.EmpresaId, c.Canal, c.ContatoIdExterno })
            .IsUnique()
            .HasFilter("\"Situacao\" <> 3")
            .HasDatabaseName("uq_atendimento_conversas_empresa_canal_contato_aberta");

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
