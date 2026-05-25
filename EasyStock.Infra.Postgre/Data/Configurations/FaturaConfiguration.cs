using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class FaturaConfiguration : IEntityTypeConfiguration<Fatura>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public void Configure(EntityTypeBuilder<Fatura> builder)
    {
        builder.ToTable("faturas");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Numero).IsRequired().HasMaxLength(20);

        builder.Property(f => f.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(f => f.Origem).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(f => f.Moeda).IsRequired().HasMaxLength(3).HasDefaultValue("BRL");

        builder.Property(f => f.SubTotal).HasColumnType("decimal(14,2)").HasDefaultValue(0m);
        builder.Property(f => f.Descontos).HasColumnType("decimal(14,2)").HasDefaultValue(0m);
        builder.Property(f => f.Acrescimos).HasColumnType("decimal(14,2)").HasDefaultValue(0m);
        builder.Property(f => f.Total).HasColumnType("decimal(14,2)").HasDefaultValue(0m);

        builder.Property(f => f.Observacoes).HasMaxLength(4000);
        builder.Property(f => f.MetadataJson).HasColumnType("jsonb");
        builder.Property(f => f.PdfStorageKey).HasMaxLength(300);

        // Snapshots como jsonb com value converter
        builder.Property(f => f.DadosFaturado)
            .HasColumnName("dados_faturado")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<DadosFaturado>(v, JsonOpts)!
            )
            .IsRequired();

        builder.Property(f => f.DadosEmissor)
            .HasColumnName("dados_emissor")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<DadosEmissor>(v, JsonOpts)!
            )
            .IsRequired();

        builder.Property(f => f.DadosFiscais)
            .HasColumnName("dados_fiscais")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOpts),
                v => v == null ? null : JsonSerializer.Deserialize<DadosFiscais>(v, JsonOpts)
            );

        // Optimistic concurrency via xmin (PG system column)
        builder.Property(f => f.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Relacionamentos
        builder.HasOne(f => f.Empresa)
            .WithMany()
            .HasForeignKey(f => f.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Cliente)
            .WithMany()
            .HasForeignKey(f => f.ClienteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(f => f.TicketRelacionado)
            .WithMany()
            .HasForeignKey(f => f.TicketRelacionadoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(f => f.Itens)
            .WithOne(i => i.Fatura)
            .HasForeignKey(i => i.FaturaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(f => f.Pagamentos)
            .WithOne(p => p.Fatura)
            .HasForeignKey(p => p.FaturaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(f => f.Eventos)
            .WithOne(e => e.Fatura)
            .HasForeignKey(e => e.FaturaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indices
        builder.HasIndex(f => new { f.EmpresaId, f.Numero })
            .IsUnique()
            .HasDatabaseName("ux_faturas_empresa_numero");
        builder.HasIndex(f => new { f.EmpresaId, f.Status })
            .HasDatabaseName("ix_faturas_empresa_status");
        builder.HasIndex(f => new { f.EmpresaId, f.DataVencimento })
            .HasDatabaseName("ix_faturas_empresa_vencimento");
        builder.HasIndex(f => new { f.Origem, f.OrigemRefId })
            .HasDatabaseName("ix_faturas_origem_ref");
        builder.HasIndex(f => f.TicketRelacionadoId)
            .HasDatabaseName("ix_faturas_ticket_relacionado")
            .HasFilter("\"TicketRelacionadoId\" IS NOT NULL");
    }
}
