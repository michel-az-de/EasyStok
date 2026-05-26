using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class FreteZonaConfiguration : IEntityTypeConfiguration<FreteZona>
{
    public void Configure(EntityTypeBuilder<FreteZona> builder)
    {
        builder.ToTable("frete_zona");
        builder.HasKey(z => z.Id);

        builder.Property(z => z.StorefrontId).IsRequired();
        builder.Property(z => z.Ordem)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(z => z.Label)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(z => z.Valor)
            .HasColumnType("decimal(10,2)")
            .IsRequired();
        builder.Property(z => z.TempoEstimadoMinutos).IsRequired();
        builder.Property(z => z.Ativa)
            .IsRequired()
            .HasDefaultValue(true);

        // Discriminator de cobertura ("cep_range" ou "bairros_lista").
        builder.Property(z => z.TipoCobertura)
            .IsRequired()
            .HasMaxLength(16);

        // CEPs sempre 8 dígitos puros (sem máscara) quando presentes.
        builder.Property(z => z.CepInicio).HasMaxLength(8);
        builder.Property(z => z.CepFim).HasMaxLength(8);

        // Lista de bairros normalizados em jsonb (lookup eventual via @>).
        builder.Property(z => z.BairrosJson)
            .HasColumnType("jsonb");

        builder.Property(z => z.CriadoEm).IsRequired();
        builder.Property(z => z.AlteradoEm).IsRequired();

        // Lookup principal: zonas do storefront ordenadas por Ordem ASC (desempate
        // quando 2 zonas cobrem o mesmo CEP/bairro — menor ordem ganha).
        builder.HasIndex(z => new { z.StorefrontId, z.Ordem })
            .HasDatabaseName("ix_frete_zona_storefront_ordem");

        // Range query helper para zonas por CEP. Filtro parcial evita índice em
        // linhas bairros_lista (CepInicio NULL).
        builder.HasIndex(z => new { z.StorefrontId, z.CepInicio, z.CepFim })
            .HasFilter("\"cep_inicio\" IS NOT NULL")
            .HasDatabaseName("ix_frete_zona_storefront_cep_range");

        // FK Storefront — CASCADE: ao deletar storefront, apaga todas as zonas.
        builder.HasOne<StorefrontEntity>()
            .WithMany()
            .HasForeignKey(z => z.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
