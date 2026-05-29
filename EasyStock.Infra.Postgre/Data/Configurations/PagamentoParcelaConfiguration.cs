using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class PagamentoParcelaConfiguration : IEntityTypeConfiguration<PagamentoParcela>
{
    public void Configure(EntityTypeBuilder<PagamentoParcela> b)
    {
        b.ToTable("pagamentos_parcela", t =>
        {
            // Check constraint XOR: exatamente UM dos dois IDs deve estar preenchido.
            t.HasCheckConstraint(
                "ck_pagamentos_parcela_lado_xor",
                "(\"ParcelaPagarId\" IS NOT NULL AND \"ParcelaReceberId\" IS NULL) " +
                "OR (\"ParcelaPagarId\" IS NULL AND \"ParcelaReceberId\" IS NOT NULL)");
        });
        b.HasKey(x => x.Id);

        b.Property(x => x.Lado).HasConversion<string>().IsRequired().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        b.Property(x => x.Metodo).IsRequired().HasMaxLength(20);
        b.Property(x => x.Valor).HasColumnType("numeric(14,2)");
        b.Property(x => x.GatewayProvedor).HasMaxLength(40);
        b.Property(x => x.GatewayTransactionId).HasMaxLength(120);
        b.Property(x => x.DadosGatewayJson).HasColumnType("jsonb");
        b.Property(x => x.RegistradoPorNome).HasMaxLength(120);
        b.Property(x => x.Observacao).HasMaxLength(500);
        b.Property(x => x.MotivoEstorno).HasMaxLength(500);

        b.HasOne(x => x.MovimentoCaixa).WithMany().HasForeignKey(x => x.MovimentoCaixaId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.EmpresaId, x.DataPagamento }).HasDatabaseName("ix_pagamentos_parcela_empresa_data");
        b.HasIndex(x => new { x.EmpresaId, x.Lado, x.Status }).HasDatabaseName("ix_pagamentos_parcela_empresa_lado_status");
        b.HasIndex(x => x.MovimentoCaixaId).HasDatabaseName("ix_pagamentos_parcela_movimento_caixa");

        // Idempotencia gateway: GatewayTransactionId UNIQUE quando preenchido (evita 2x mesmo pagamento)
        b.HasIndex(x => new { x.GatewayProvedor, x.GatewayTransactionId })
            .IsUnique()
            .HasDatabaseName("ux_pagamentos_parcela_gateway_tx")
            .HasFilter("\"GatewayTransactionId\" IS NOT NULL");
    }
}
