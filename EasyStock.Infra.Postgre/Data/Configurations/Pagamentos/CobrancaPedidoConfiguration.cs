using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Infra.Postgre.Data.Configurations.Pagamentos;

/// <summary>Cobrança do pedido pelo Mercado Pago ou na entrega (S11).</summary>
public class CobrancaPedidoConfiguration : IEntityTypeConfiguration<CobrancaPedido>
{
    public void Configure(EntityTypeBuilder<CobrancaPedido> b)
    {
        b.ToTable("cobrancas_pedido");
        b.HasKey(c => c.Id);

        b.Property(c => c.EmpresaId).IsRequired();
        b.Property(c => c.PedidoId).IsRequired();
        b.Property(c => c.Provedor).IsRequired().HasMaxLength(CobrancaPedido.ProvedorTamanhoMaximo);
        b.Property(c => c.ReferenciaExterna).HasMaxLength(CobrancaPedido.ReferenciaTamanhoMaximo);
        b.Property(c => c.LinkPagamento).HasMaxLength(CobrancaPedido.LinkTamanhoMaximo);
        b.Property(c => c.Valor).HasColumnType("numeric(14,2)").IsRequired();
        b.Property(c => c.ValorPago).HasColumnType("numeric(14,2)");
        b.Property(c => c.Status).HasConversion<int>().IsRequired();
        b.Property(c => c.PagamentoExternoId).HasMaxLength(CobrancaPedido.ReferenciaTamanhoMaximo);
        b.Property(c => c.MetodoPagamento).HasMaxLength(CobrancaPedido.MetodoTamanhoMaximo);
        b.Property(c => c.Motivo).HasMaxLength(CobrancaPedido.MotivoTamanhoMaximo);
        b.Property(c => c.Tentativa).IsRequired();
        b.Property(c => c.CriadaEm).IsRequired();

        b.Ignore(c => c.EhOnline);
        b.Ignore(c => c.EstaPendente);

        // A preferência do provedor aponta para uma cobrança só. Na entrega não tem referência (NULL não colide).
        b.HasIndex(c => new { c.Provedor, c.ReferenciaExterna })
            .IsUnique()
            .HasDatabaseName("uq_cobrancas_pedido_provedor_referencia");

        // Cobranças do pedido (confirmação, troca de forma, reemissão).
        b.HasIndex(c => new { c.PedidoId, c.Status })
            .HasDatabaseName("ix_cobrancas_pedido_pedido_status");

        // Varredura do CobrancaPedidoJob: só as pendentes (Status = 1), pela expiração.
        b.HasIndex(c => c.ExpiraEm)
            .HasFilter("\"Status\" = 1")
            .HasDatabaseName("ix_cobrancas_pedido_pendentes_expira");

        // FK Pedido — CASCADE: a cobrança não existe sem o pedido (mesmo comportamento de pedido_pagamentos).
        b.HasOne<Pedido>()
            .WithMany()
            .HasForeignKey(c => c.PedidoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
