using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

/// <summary>I9.1 (ADR-0061 D5): corrida externa de uma viagem, com o custo. #2158 (ADR-0064) acrescenta a corrida da API.</summary>
public class CorridaExternaConfiguration : IEntityTypeConfiguration<CorridaExterna>
{
    public void Configure(EntityTypeBuilder<CorridaExterna> builder)
    {
        builder.ToTable("corridas_externas", t =>
            t.HasCheckConstraint("ck_corridas_externas_custo", "\"CustoCentavos\" >= 0"));
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.Provedor).HasConversion<int>().IsRequired();
        builder.Property(c => c.Origem).HasConversion<int>().IsRequired();
        builder.Property(c => c.CustoCentavos).IsRequired();
        builder.Property(c => c.Motorista).HasMaxLength(CorridaExterna.MotoristaTamanhoMaximo);
        builder.Property(c => c.RegistradoPorUsuarioId).IsRequired();
        builder.Property(c => c.RegistradoEm).IsRequired();

        // A viagem não é apagada (desfazer só muda a situação); Restrict protege o histórico de custo.
        builder.HasOne<Viagem>().WithMany().HasForeignKey(c => c.ViagemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.EmpresaId, c.ViagemId }).HasDatabaseName("ix_corridas_externas_empresa_viagem");

        // Corrida chamada pela API (#2158, ADR-0064): colunas nulas na manual.
        builder.Property(c => c.Status).HasConversion<int?>();
        builder.Property(c => c.Veiculo).HasConversion<int?>();
        builder.Property(c => c.CotacaoId).HasMaxLength(CorridaExterna.IdExternoTamanhoMaximo);
        builder.Property(c => c.IdExterno).HasMaxLength(CorridaExterna.IdExternoTamanhoMaximo);
        builder.Property(c => c.IdExternoAnterior).HasMaxLength(CorridaExterna.IdExternoTamanhoMaximo);
        builder.Property(c => c.LinkRastreio).HasMaxLength(CorridaExterna.LinkRastreioTamanhoMaximo);
        builder.Property(c => c.MotoristaIdExterno).HasMaxLength(CorridaExterna.IdExternoTamanhoMaximo);
        builder.Property(c => c.MotoristaTelefone).HasMaxLength(CorridaExterna.TelefoneTamanhoMaximo);
        builder.Property(c => c.MotoristaPlaca).HasMaxLength(CorridaExterna.PlacaTamanhoMaximo);
        builder.Ignore(c => c.Ativa);

        // R4: dois cliques ou dois hosts não abrem duas corridas em curso na mesma viagem. Os números são
        // StatusCorrida: Solicitando, ProcurandoMotorista, MotoristaACaminho e Coletada.
        builder.HasIndex(c => c.ViagemId, "IX_corridas_externas_ViagemId");
        builder.HasIndex(c => c.ViagemId, "ux_corridas_externas_viagem_ativa").IsUnique()
            .HasFilter("\"Status\" IN (1, 2, 3, 4)");
        // O webhook chega só com o id do provedor: é por ele que se acha a corrida (e a loja).
        builder.HasIndex(c => new { c.Provedor, c.IdExterno }, "ux_corridas_externas_provedor_id_externo").IsUnique()
            .HasFilter("\"IdExterno\" IS NOT NULL");
    }
}