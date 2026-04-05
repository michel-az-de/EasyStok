using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
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
            builder.Property(iv => iv.VariacaoSnapshot).HasMaxLength(180);
            builder.Property(iv => iv.Quantidade)
                .HasConversion(q => q.Value, value => Quantidade.From(value));
            builder.Property(iv => iv.PrecoUnitario)
                .HasConversion(d => d.Valor, value => Dinheiro.FromDecimal(value))
                .HasColumnType("decimal(18,2)");
            builder.Property(iv => iv.PrecoTotal)
                .HasConversion(d => d.Valor, value => Dinheiro.FromDecimal(value))
                .HasColumnType("decimal(18,2)");

            builder.HasOne(iv => iv.Venda).WithMany(v => v.ItensVenda).HasForeignKey(iv => iv.VendaId);
            builder.HasOne(iv => iv.ItemEstoque).WithMany(i => i.ItensVenda).HasForeignKey(iv => iv.ItemEstoqueId);
            builder.HasOne(iv => iv.Produto).WithMany(p => p.ItensVenda).HasForeignKey(iv => iv.ProdutoId);
            builder.HasOne(iv => iv.ProdutoVariacao).WithMany(v => v.ItensVenda).HasForeignKey(iv => iv.ProdutoVariacaoId).IsRequired(false);
        }
    }
}
