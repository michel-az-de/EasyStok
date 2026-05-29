namespace EasyStock.Infra.Postgre.Data.Configurations;

public class FaturaPagamentoConfiguration : IEntityTypeConfiguration<FaturaPagamento>
{
    public void Configure(EntityTypeBuilder<FaturaPagamento> builder)
    {
        builder.ToTable("fatura_pagamentos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.EmpresaId).IsRequired();
        builder.Property(p => p.Metodo).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Valor).HasColumnType("decimal(14,2)");
        builder.Property(p => p.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(p => p.GatewayProvedor).IsRequired().HasMaxLength(50);
        builder.Property(p => p.GatewayTransactionId).HasMaxLength(120);
        builder.Property(p => p.DadosGatewayJson).HasColumnType("jsonb");
        builder.Property(p => p.Observacao).HasMaxLength(2000);
        builder.Property(p => p.RegistradoPorNome).HasMaxLength(120);

        // Onda P0 Payment Orchestration
        builder.Property(p => p.TotalTentativas).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.UltimaErrorCategory)
            .HasConversion<byte?>()
            .HasColumnName("UltimaErrorCategory");
        builder.Property(p => p.ClientIdempotencyKey).HasMaxLength(80);

        // FK explicita — pagamento nao existe sem fatura (Cascade).
        builder.HasOne(p => p.Fatura)
            .WithMany(f => f.Pagamentos)
            .HasForeignKey(p => p.FaturaId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK opcional pra Usuario via RegistradoPorUserId. Sem navigation
        // property na entity — usamos overload sem nav. SetNull preserva
        // o pagamento mesmo se o usuario for anonimizado/excluido (LGPD).
        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.RegistradoPorUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(p => p.FaturaId).HasDatabaseName("ix_fatura_pagamentos_fatura_id");
        builder.HasIndex(p => new { p.GatewayProvedor, p.GatewayTransactionId })
            .HasDatabaseName("ix_fatura_pagamentos_gateway_tx")
            .HasFilter("\"GatewayTransactionId\" IS NOT NULL");
        builder.HasIndex(p => new { p.FaturaId, p.Status })
            .HasDatabaseName("ix_fatura_pagamentos_fatura_status");

        // Indice por empresa (Global Query Filter automatico aplica)
        builder.HasIndex(p => new { p.EmpresaId, p.Status })
            .HasDatabaseName("ix_fatura_pagamentos_empresa_status");

        // UNIQUE parcial: Idempotency-Key opcional do cliente — segunda request com
        // mesma key retorna o mesmo pagamento.
        builder.HasIndex(p => new { p.EmpresaId, p.ClientIdempotencyKey })
            .HasDatabaseName("ux_fatura_pagamentos_empresa_client_idempotency")
            .HasFilter("\"ClientIdempotencyKey\" IS NOT NULL")
            .IsUnique();
    }
}
