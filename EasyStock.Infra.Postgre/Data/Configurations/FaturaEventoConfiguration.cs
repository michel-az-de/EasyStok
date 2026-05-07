using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class FaturaEventoConfiguration : IEntityTypeConfiguration<FaturaEvento>
{
    public void Configure(EntityTypeBuilder<FaturaEvento> builder)
    {
        builder.ToTable("fatura_eventos");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Tipo).HasConversion<string>().IsRequired().HasMaxLength(40);
        builder.Property(e => e.ValorAntes).HasMaxLength(500);
        builder.Property(e => e.ValorDepois).HasMaxLength(500);
        builder.Property(e => e.MetadadosJson).HasColumnType("jsonb");
        builder.Property(e => e.UsuarioNome).HasMaxLength(120);
        builder.Property(e => e.Origem).HasMaxLength(20);

        builder.HasIndex(e => e.FaturaId).HasDatabaseName("ix_fatura_eventos_fatura_id");
        builder.HasIndex(e => new { e.FaturaId, e.OcorridoEm })
            .HasDatabaseName("ix_fatura_eventos_fatura_ocorrido");
    }
}
