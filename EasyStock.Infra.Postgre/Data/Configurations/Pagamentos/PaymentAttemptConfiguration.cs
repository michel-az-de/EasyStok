using EasyStock.Domain.Entities.Pagamentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Pagamentos;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> b)
    {
        b.ToTable("pagamento_attempts");
        b.HasKey(p => p.Id);

        b.Property(p => p.EmpresaId).IsRequired();
        b.Property(p => p.FaturaPagamentoId).IsRequired();
        b.Property(p => p.FaturaId).IsRequired();
        b.Property(p => p.Provedor).IsRequired().HasMaxLength(40);
        b.Property(p => p.Metodo).IsRequired().HasMaxLength(20);
        b.Property(p => p.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        b.Property(p => p.Tentativa).IsRequired();
        b.Property(p => p.IniciadoEm).IsRequired();
        b.Property(p => p.GatewayTransactionId).HasMaxLength(80);
        b.Property(p => p.ErrorCategory).HasConversion<byte?>();
        b.Property(p => p.ErrorCode).HasMaxLength(60);
        b.Property(p => p.ErrorMessage).HasMaxLength(500);
        b.Property(p => p.IdempotencyKey).IsRequired().HasMaxLength(64);
        b.Property(p => p.ClientIdempotencyKey).HasMaxLength(80);
        b.Property(p => p.RoutingMotivo).IsRequired().HasMaxLength(60);
        b.Property(p => p.MetadataJson).HasColumnType("jsonb");

        // RowVersion via xmin (Postgres system column)
        b.Property(p => p.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // FK ao FaturaPagamento (cascade — attempt morre com pagamento)
        b.HasOne(p => p.FaturaPagamento)
            .WithMany()
            .HasForeignKey(p => p.FaturaPagamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indices

        // Idempotencia: (EmpresaId, IdempotencyKey) UNIQUE — bloqueia duplo charge
        b.HasIndex(p => new { p.EmpresaId, p.IdempotencyKey })
            .HasDatabaseName("ux_pagamento_attempts_empresa_idempotency")
            .IsUnique();

        // Invariante critica: 1 Sucesso por FaturaPagamento (partial unique do Postgres).
        b.HasIndex(p => p.FaturaPagamentoId)
            .HasDatabaseName("ux_pagamento_attempts_pagamento_sucesso")
            .HasFilter("\"Status\" = 'Sucesso'")
            .IsUnique();

        // Bloqueia 2 attempts apontando para mesma cobranca de gateway.
        b.HasIndex(p => new { p.Provedor, p.GatewayTransactionId })
            .HasDatabaseName("ux_pagamento_attempts_gateway_tx")
            .HasFilter("\"GatewayTransactionId\" IS NOT NULL")
            .IsUnique();

        // Reconciliador (P1) faz queries por (Status, ProximaConsultaEm).
        b.HasIndex(p => new { p.Status, p.ProximaConsultaEm })
            .HasDatabaseName("ix_pagamento_attempts_status_proxima_consulta");

        // Listagem por fatura.
        b.HasIndex(p => new { p.EmpresaId, p.FaturaPagamentoId, p.Tentativa })
            .HasDatabaseName("ix_pagamento_attempts_empresa_pagamento_tentativa");

        // Dashboards de erro por gateway (parcial onde nao foi sucesso).
        b.HasIndex(p => new { p.EmpresaId, p.Provedor, p.IniciadoEm })
            .HasDatabaseName("ix_pagamento_attempts_empresa_provedor_inicio")
            .HasFilter("\"Status\" <> 'Sucesso'");
    }
}
