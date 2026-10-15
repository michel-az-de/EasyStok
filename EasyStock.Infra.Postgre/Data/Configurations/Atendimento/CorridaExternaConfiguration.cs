using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

/// <summary>I9.1 (ADR-0061 D5): corrida externa de uma viagem, com o custo. A E10 estende esta tabela.</summary>
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
    }
}