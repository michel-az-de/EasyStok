using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

/// <summary>S67 (#2534): registro dos turnos do Assistente, por empresa, com RLS (migration). Sem texto nem telefone.</summary>
public class TurnoAgenteConfiguration : IEntityTypeConfiguration<TurnoAgente>
{
    public void Configure(EntityTypeBuilder<TurnoAgente> builder)
    {
        builder.ToTable("atendimento_turnos_agente");
        builder.HasKey(t => t.Id);
        // PK gerada no app (ADR-0028): nunca inferir UPDATE de linha inexistente.
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Ignore(t => t.TotalTokens);
        builder.Ignore(t => t.Ferramentas);

        builder.Property(t => t.EmpresaId).IsRequired();
        builder.Property(t => t.ConversaId).IsRequired();
        builder.Property(t => t.Modelo).HasMaxLength(TurnoAgente.ModeloTamanhoMaximo).IsRequired();
        builder.Property(t => t.CustoEstimado).HasPrecision(18, 8);
        builder.Property(t => t.FerramentasJson).HasColumnName("Ferramentas").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.Desfecho).HasConversion<int>().IsRequired();
        builder.Property(t => t.CriadoEm).IsRequired();

        // Histórico e consumo do mês leem por empresa e data.
        builder.HasIndex(t => new { t.EmpresaId, t.CriadoEm })
            .HasDatabaseName("ix_atendimento_turnos_agente_empresa_criado");
    }
}
