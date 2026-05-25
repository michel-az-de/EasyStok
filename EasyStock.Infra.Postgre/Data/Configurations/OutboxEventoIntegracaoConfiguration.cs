using EasyStock.Domain.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// EF mapping da tabela <c>outbox_evento_integracao</c>. Filtro multi-tenant
/// global aplicado automaticamente (entity tem <c>EmpresaId</c>) — uso
/// admin/dispatcher cross-tenant requer <c>IgnoreQueryFilters</c>.
/// </summary>
public class OutboxEventoIntegracaoConfiguration : IEntityTypeConfiguration<OutboxEventoIntegracao>
{
    public void Configure(EntityTypeBuilder<OutboxEventoIntegracao> b)
    {
        b.ToTable("outbox_evento_integracao");
        b.HasKey(o => o.Id);

        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(o => o.TipoEvento).HasColumnName("tipo_evento").HasMaxLength(120).IsRequired();
        b.Property(o => o.AggregateType).HasColumnName("aggregate_type").HasMaxLength(60).IsRequired();
        b.Property(o => o.AggregateId).HasColumnName("aggregate_id").IsRequired();
        b.Property(o => o.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        b.Property(o => o.PayloadSchemaVersion).HasColumnName("payload_schema_version").IsRequired();
        b.Property(o => o.CorrelationId).HasColumnName("correlation_id").HasMaxLength(120);
        b.Property(o => o.CausationEventId).HasColumnName("causation_event_id");

        b.Property(o => o.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(o => o.Tentativas).HasColumnName("tentativas").IsRequired();
        b.Property(o => o.MaxTentativas).HasColumnName("max_tentativas").IsRequired();
        b.Property(o => o.ProximaTentativaEm).HasColumnName("proxima_tentativa_em").IsRequired();
        b.Property(o => o.ProcessadoEm).HasColumnName("processado_em");
        b.Property(o => o.ErroUltimaTentativa).HasColumnName("erro_ultima_tentativa").HasColumnType("text");

        b.Property(o => o.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).IsRequired();
        b.Property(o => o.ShardKey).HasColumnName("shard_key").IsRequired();
        b.Property(o => o.CriadoEm).HasColumnName("criado_em").IsRequired();

        // IdempotencyKey único — caller que reprocessa idempotentemente reescreve
        // mesma row; tentar inserir duplicata gera UniqueViolation que callers
        // tratam como "evento já enfileirado".
        b.HasIndex(o => o.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ix_outbox_evento_integracao_idempotency_key");

        // Índice principal do dispatcher: próximos pendentes por shard.
        // Filtro WHERE status = 1 (Pendente) reduz tamanho do índice e
        // mantém performance estável mesmo com volume alto de Enviados.
        b.HasIndex(o => new { o.ShardKey, o.ProximaTentativaEm })
            .HasFilter("status = 1")
            .HasDatabaseName("ix_outbox_evento_integracao_pendentes");

        // Índice secundário pro painel admin (lista por empresa + status).
        b.HasIndex(o => new { o.EmpresaId, o.Status, o.CriadoEm })
            .HasDatabaseName("ix_outbox_evento_integracao_empresa_status");

        b.HasOne(o => o.Empresa)
            .WithMany()
            .HasForeignKey(o => o.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
