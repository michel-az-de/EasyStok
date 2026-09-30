using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class MensagemProgramadaConfiguration : IEntityTypeConfiguration<MensagemProgramada>
{
    public void Configure(EntityTypeBuilder<MensagemProgramada> builder)
    {
        builder.ToTable("mensagens_programadas");
        builder.HasKey(m => m.Id);

        builder.Ignore(m => m.Modelo);
        builder.Property(m => m.EmpresaId).IsRequired();
        builder.Property(m => m.ClienteId).IsRequired();
        builder.Property(m => m.Canal).HasConversion<int>().IsRequired();
        builder.Property(m => m.Finalidade).HasConversion<int>().IsRequired();
        builder.Property(m => m.Situacao).HasConversion<int>().IsRequired();
        builder.Property(m => m.Texto).HasMaxLength(MensagemProgramada.TextoTamanhoMaximo);
        builder.Property(m => m.ModeloNome).HasMaxLength(120);
        builder.Property(m => m.ModeloIdioma).HasMaxLength(10);
        builder.Property(m => m.ModeloParametrosJson).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.IdExterno).HasMaxLength(Mensagem.ExternoIdTamanhoMaximo);
        builder.Property(m => m.Erro).HasMaxLength(MensagemProgramada.ErroTamanhoMaximo);
        builder.Property(m => m.AgendadaPara).IsRequired();
        builder.Property(m => m.CriadaEm).IsRequired();
        builder.Property(m => m.AlteradaEm).IsRequired();

        // Disparador: agendadas vencidas, das mais antigas para as mais novas.
        builder.HasIndex(m => new { m.Situacao, m.AgendadaPara })
            .HasDatabaseName("ix_mensagens_programadas_situacao_agendada_para");
        // Console: por cliente.
        builder.HasIndex(m => new { m.EmpresaId, m.ClienteId, m.AgendadaPara })
            .HasDatabaseName("ix_mensagens_programadas_empresa_cliente");

        builder.HasOne<Domain.Entities.Cliente>()
            .WithMany()
            .HasForeignKey(m => m.ClienteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
