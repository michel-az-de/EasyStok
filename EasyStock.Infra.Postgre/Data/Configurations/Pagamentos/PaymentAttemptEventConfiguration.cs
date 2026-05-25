using EasyStock.Domain.Entities.Pagamentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Pagamentos;

public class PaymentAttemptEventConfiguration : IEntityTypeConfiguration<PaymentAttemptEvent>
{
    public void Configure(EntityTypeBuilder<PaymentAttemptEvent> b)
    {
        b.ToTable("pagamento_attempt_events");
        b.HasKey(e => e.Id);

        b.Property(e => e.PaymentAttemptId).IsRequired();
        b.Property(e => e.EmpresaId).IsRequired();
        b.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.ToStatus).HasConversion<string>().IsRequired().HasMaxLength(30);
        b.Property(e => e.Motivo).IsRequired().HasMaxLength(60);
        b.Property(e => e.GatewayResponseJson).HasColumnType("jsonb");
        b.Property(e => e.OcorridoEm).IsRequired();
        b.Property(e => e.CorrelationId).HasMaxLength(80);

        // FK cascade — eventos morrem com o attempt.
        b.HasOne(e => e.PaymentAttempt)
            .WithMany()
            .HasForeignKey(e => e.PaymentAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(e => new { e.PaymentAttemptId, e.OcorridoEm })
            .HasDatabaseName("ix_pagamento_attempt_events_attempt_ocorrido");
    }
}
