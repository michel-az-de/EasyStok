using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// Configuration introduzida em F1 para adicionar a FK opcional para
/// <see cref="Fatura"/>. Tabela ja existia (mapeada por convencao via
/// <c>CobrancasAssinatura</c> DbSet) — esta configuracao apenas formaliza a
/// FK e indices novos sem reescrever colunas existentes.
///
/// <para>
/// Body comentado em #if false: commit 2b9c1f2 reativou os DbSets de Fatura,
/// mas <see cref="CobrancaAssinatura"/> ainda nao tem as propriedades
/// <c>Fatura</c> e <c>FaturaId</c> reintroduzidas. Reativar este config junto
/// com a volta dessas duas propriedades em CobrancaAssinatura.cs.
/// </para>
/// </summary>
public class CobrancaAssinaturaConfiguration : IEntityTypeConfiguration<CobrancaAssinatura>
{
    public void Configure(EntityTypeBuilder<CobrancaAssinatura> builder)
    {
        builder.ToTable("CobrancasAssinatura");

#if false
        builder.HasOne(c => c.Fatura)
            .WithMany()
            .HasForeignKey(c => c.FaturaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.FaturaId)
            .HasDatabaseName("ix_cobrancas_assinatura_fatura_id")
            .HasFilter("\"FaturaId\" IS NOT NULL");
#endif
    }
}
