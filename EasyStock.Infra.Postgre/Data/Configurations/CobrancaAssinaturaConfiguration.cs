using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// Configuration introduzida em F1 para adicionar a FK opcional para
/// <see cref="Fatura"/> (link da convivencia). Tabela ja existia (mapeada por
/// convencao via <c>CobrancasAssinatura</c> DbSet) — esta configuracao apenas
/// formaliza a FK e indices novos sem reescrever colunas existentes.
/// </summary>
public class CobrancaAssinaturaConfiguration : IEntityTypeConfiguration<CobrancaAssinatura>
{
    public void Configure(EntityTypeBuilder<CobrancaAssinatura> builder)
    {
        builder.ToTable("CobrancasAssinatura");

        builder.HasOne(c => c.Fatura)
            .WithMany()
            .HasForeignKey(c => c.FaturaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.FaturaId)
            .HasDatabaseName("ix_cobrancas_assinatura_fatura_id")
            .HasFilter("\"FaturaId\" IS NOT NULL");
    }
}
