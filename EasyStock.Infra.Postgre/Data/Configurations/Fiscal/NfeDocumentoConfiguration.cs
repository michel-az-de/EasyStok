using System.Text.Json;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public class NfeDocumentoConfiguration : IEntityTypeConfiguration<NfeDocumento>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public void Configure(EntityTypeBuilder<NfeDocumento> builder)
    {
        builder.ToTable("nfe_documentos");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Modelo).IsRequired().HasMaxLength(2);
        builder.Property(n => n.Status).HasConversion<string>().IsRequired().HasMaxLength(40);
        builder.Property(n => n.ChaveAcesso).HasMaxLength(44);
        builder.Property(n => n.ProtocoloAutorizacao).HasMaxLength(40);
        builder.Property(n => n.MotivoRejeicao).HasMaxLength(2000);
        builder.Property(n => n.XmlAssinadoStorageKey).HasMaxLength(300);
        builder.Property(n => n.DanfeUrl).HasMaxLength(500);

        // Snapshots como jsonb com value converter (espelha padrao Fatura).
        builder.Property(n => n.DadosEmitente)
            .HasColumnName("dados_emitente")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<DadosEmissor>(v, JsonOpts)!)
            .IsRequired();

        builder.Property(n => n.DadosDestinatario)
            .HasColumnName("dados_destinatario")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOpts),
                v => v == null ? null : JsonSerializer.Deserialize<DadosFaturado>(v, JsonOpts));

        // Total da nota (Dinheiro VO -> numeric(14,2)).
        builder.Property(n => n.TotalNota)
            .HasConversion(
                v => v.Valor,
                v => Dinheiro.FromDecimal(v))
            .HasColumnType("numeric(14,2)")
            .IsRequired();

        // Optimistic concurrency via xmin (Postgres system column).
        builder.Property(n => n.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Relacionamentos
        builder.HasOne(n => n.Empresa)
            .WithMany()
            .HasForeignKey(n => n.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Pedido)
            .WithMany()
            .HasForeignKey(n => n.PedidoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(n => n.Itens)
            .WithOne(i => i.NfeDocumento)
            .HasForeignKey(i => i.NfeDocumentoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(n => n.Eventos)
            .WithOne(e => e.NfeDocumento)
            .HasForeignKey(e => e.NfeDocumentoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Idempotencia: nao duplicar autorizacao para a mesma chave.
        builder.HasIndex(n => new { n.EmpresaId, n.ChaveAcesso })
            .IsUnique()
            .HasDatabaseName("ux_nfe_documentos_empresa_chave_acesso")
            .HasFilter("\"ChaveAcesso\" IS NOT NULL");

        // Idempotencia: nao reutilizar numero ja consumido por modelo+serie.
        builder.HasIndex(n => new { n.EmpresaId, n.Modelo, n.Serie, n.Numero })
            .IsUnique()
            .HasDatabaseName("ux_nfe_documentos_empresa_modelo_serie_numero");

        builder.HasIndex(n => new { n.EmpresaId, n.Status })
            .HasDatabaseName("ix_nfe_documentos_empresa_status");

        builder.HasIndex(n => n.PedidoId)
            .HasDatabaseName("ix_nfe_documentos_pedido");
    }
}
