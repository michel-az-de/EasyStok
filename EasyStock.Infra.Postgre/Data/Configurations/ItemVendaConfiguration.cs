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
            builder.Property(iv => iv.DescricaoSnapshot).HasMaxLength(500);
            builder.Property(iv => iv.VariacaoSnapshot).HasMaxLength(180);
            builder.Property(iv => iv.Quantidade)
                .HasConversion(
                    q => q == null ? 0 : q.Value,
                    value => value >= 0 ? Quantidade.From(value) : Quantidade.Zero);
            builder.Property(iv => iv.PrecoUnitario)
                .HasConversion(
                    d => d == null ? 0m : d.Valor,
                    value => value >= 0 ? Dinheiro.FromDecimal(value) : Dinheiro.Zero)
                .HasColumnType("decimal(18,2)");
            builder.Property(iv => iv.PrecoTotal)
                .HasConversion(
                    d => d == null ? 0m : d.Valor,
                    value => value >= 0 ? Dinheiro.FromDecimal(value) : Dinheiro.Zero)
                .HasColumnType("decimal(18,2)");

            builder.HasOne(iv => iv.Venda).WithMany(v => v.ItensVenda).HasForeignKey(iv => iv.VendaId);
            builder.HasOne(iv => iv.ItemEstoque).WithMany(i => i.ItensVenda).HasForeignKey(iv => iv.ItemEstoqueId);
            builder.HasOne(iv => iv.Produto).WithMany(p => p.ItensVenda).HasForeignKey(iv => iv.ProdutoId);
            builder.HasOne(iv => iv.ProdutoVariacao).WithMany(v => v.ItensVenda).HasForeignKey(iv => iv.ProdutoVariacaoId).IsRequired(false);

            // Índice para cálculo de receita por período (analytics)
            builder.HasIndex(iv => iv.VendaId)
                .HasDatabaseName("ix_itens_venda_venda_id");
        }
    }
}
