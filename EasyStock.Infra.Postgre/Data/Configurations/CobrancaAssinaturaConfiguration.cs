using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// Configuration introduzida em F1 para adicionar a FK opcional para
/// <see cref="Fatura"/>. Tabela ja existia (mapeada por convencao via
/// <c>CobrancasAssinatura</c> DbSet) — esta configuracao apenas formaliza a
/// FK e indices novos sem reescrever colunas existentes. Tambem mantem a
/// precisao 14,2 aplicada por <c>Onda2_HasPrecisionCobrancaAssinatura</c>.
/// </summary>
public class CobrancaAssinaturaConfiguration : IEntityTypeConfiguration<CobrancaAssinatura>
{
    public void Configure(EntityTypeBuilder<CobrancaAssinatura> builder)
    {
        builder.ToTable("CobrancasAssinatura");

        // Mantem precisao 14,2 aplicada em Onda 2.2. Sem isto, EF detecta drift
        // e tenta voltar a coluna para numeric (sem escala).
        builder.Property(c => c.Valor).HasPrecision(14, 2);

        builder.HasOne(c => c.Fatura)
            .WithMany()
            .HasForeignKey(c => c.FaturaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.FaturaId)
            .HasDatabaseName("ix_cobrancas_assinatura_fatura_id")
            .HasFilter("\"FaturaId\" IS NOT NULL");
    }
}
