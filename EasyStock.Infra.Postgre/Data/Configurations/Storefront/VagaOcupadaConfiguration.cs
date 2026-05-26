using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class VagaOcupadaConfiguration : IEntityTypeConfiguration<VagaOcupada>
{
    public void Configure(EntityTypeBuilder<VagaOcupada> builder)
    {
        builder.ToTable("vaga_ocupada");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.JanelaEntregaId).IsRequired();
        builder.Property(v => v.DataEntrega).IsRequired();
        builder.Property(v => v.PedidoId).IsRequired();
        builder.Property(v => v.OcupadoEm).IsRequired();
        builder.Property(v => v.LiberadoEm);

        builder.Property(v => v.MotivoLiberacao)
            .HasMaxLength(200);

        // ADR-0014: pedido só pode ter UMA vaga ativa. Filtered unique index garante isso
        // mesmo após replicação (libera antiga, cria nova). Postgres-specific.
        builder.HasIndex(v => v.PedidoId)
            .IsUnique()
            .HasFilter("\"LiberadoEm\" IS NULL")
            .HasDatabaseName("uq_vaga_ativa_por_pedido");

        // ADR-0014: lookup de capacidade efetiva (COUNT WHERE liberado_em IS NULL).
        builder.HasIndex(v => new { v.JanelaEntregaId, v.DataEntrega })
            .HasFilter("\"LiberadoEm\" IS NULL")
            .HasDatabaseName("ix_vaga_ativa_por_janela_data");

        // FK JanelaEntrega — RESTRICT: não deixa apagar janela que tem vagas ocupadas.
        // Forces explicit cleanup.
        builder.HasOne<JanelaEntrega>()
            .WithMany()
            .HasForeignKey(v => v.JanelaEntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK Pedido — RESTRICT: vaga é histórico/auditoria, não some quando Pedido vai pra
        // estado terminal. Soft-link via LiberadoEm.
        builder.HasOne<Pedido>()
            .WithMany()
            .HasForeignKey(v => v.PedidoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
