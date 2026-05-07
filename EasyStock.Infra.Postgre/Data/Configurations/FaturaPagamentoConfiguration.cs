using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class FaturaPagamentoConfiguration : IEntityTypeConfiguration<FaturaPagamento>
{
    public void Configure(EntityTypeBuilder<FaturaPagamento> builder)
    {
        builder.ToTable("fatura_pagamentos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Metodo).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Valor).HasColumnType("decimal(14,2)");
        builder.Property(p => p.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(p => p.GatewayProvedor).IsRequired().HasMaxLength(50);
        builder.Property(p => p.GatewayTransactionId).HasMaxLength(120);
        builder.Property(p => p.DadosGatewayJson).HasColumnType("jsonb");
        builder.Property(p => p.Observacao).HasMaxLength(2000);
        builder.Property(p => p.RegistradoPorNome).HasMaxLength(120);

        builder.HasIndex(p => p.FaturaId).HasDatabaseName("ix_fatura_pagamentos_fatura_id");
        builder.HasIndex(p => new { p.GatewayProvedor, p.GatewayTransactionId })
            .HasDatabaseName("ix_fatura_pagamentos_gateway_tx")
            .HasFilter("\"GatewayTransactionId\" IS NOT NULL");
        builder.HasIndex(p => new { p.FaturaId, p.Status })
            .HasDatabaseName("ix_fatura_pagamentos_fatura_status");
    }
}
