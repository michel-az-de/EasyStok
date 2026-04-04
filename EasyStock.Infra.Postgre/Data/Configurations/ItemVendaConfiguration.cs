using EasyStok.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ItemVendaConfiguration : IEntityTypeConfiguration<ItemVenda>
    {
        public void Configure(EntityTypeBuilder<ItemVenda> builder)
        {
            builder.ToTable("itens_venda");
            builder.HasKey(iv => iv.Id);
            builder.Property(iv => iv.PrecoUnitario).HasColumnType("decimal(18,2)");
            builder.Property(iv => iv.PrecoTotal).HasColumnType("decimal(18,2)");

            builder.HasOne(iv => iv.Venda).WithMany(v => v.ItensVenda).HasForeignKey(iv => iv.VendaId);
            builder.HasOne(iv => iv.ItemEstoque).WithMany(i => i.ItensVenda).HasForeignKey(iv => iv.ItemEstoqueId);
            builder.HasOne(iv => iv.Produto).WithMany(p => p.ItensVenda).HasForeignKey(iv => iv.ProdutoId);
        }
    }
}
