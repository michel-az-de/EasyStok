using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class PedidoFornecedorConfiguration : IEntityTypeConfiguration<PedidoFornecedor>
{
    public void Configure(EntityTypeBuilder<PedidoFornecedor> builder)
    {
        builder.ToTable("pedidos_fornecedor");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(x => x.ValorEstimado)
            .HasColumnType("decimal(18,2)");
        builder.Property(x => x.Canal).HasMaxLength(100);
        builder.Property(x => x.Tracking).HasMaxLength(120);
        builder.Property(x => x.Observacoes).HasColumnType("text");

        builder.HasOne(x => x.Fornecedor)
            .WithMany()
            .HasForeignKey(x => x.FornecedorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.EmpresaId, x.FornecedorId, x.Status });
        builder.HasIndex(x => new { x.EmpresaId, x.DataPedido });
    }
}
