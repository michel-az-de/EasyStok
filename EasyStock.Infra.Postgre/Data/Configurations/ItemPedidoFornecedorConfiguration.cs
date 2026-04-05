using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ItemPedidoFornecedorConfiguration : IEntityTypeConfiguration<ItemPedidoFornecedor>
{
    public void Configure(EntityTypeBuilder<ItemPedidoFornecedor> builder)
    {
        builder.ToTable("itens_pedido_fornecedor");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Descricao).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Quantidade).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CustoUnitario).HasColumnType("decimal(18,2)");

        builder.HasOne(x => x.Pedido)
            .WithMany(p => p.Itens)
            .HasForeignKey(x => x.PedidoFornecedorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.PedidoFornecedorId);
        builder.HasIndex(x => new { x.EmpresaId, x.ProdutoId });
    }
}
