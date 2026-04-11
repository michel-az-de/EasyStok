using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class MovimentacaoEstoqueConfiguration : IEntityTypeConfiguration<MovimentacaoEstoque>
    {
        public void Configure(EntityTypeBuilder<MovimentacaoEstoque> builder)
        {
            builder.ToTable("movimentacoes_estoque");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Tipo).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(m => m.Natureza).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(m => m.Quantidade)
                .HasConversion(q => q.Value, value => Quantidade.From(value));
            builder.Property(m => m.ValorUnitario)
                .HasConversion(
                    d => d == null ? (decimal?)null : d.Valor,
                    value => value.HasValue ? Dinheiro.FromDecimal(value.Value) : null)
                .HasColumnType("decimal(18,2)");
            builder.Property(m => m.ValorTotal)
                .HasConversion(
                    d => d == null ? (decimal?)null : d.Valor,
                    value => value.HasValue ? Dinheiro.FromDecimal(value.Value) : null)
                .HasColumnType("decimal(18,2)");

            builder.HasOne(m => m.Empresa).WithMany(e => e.Movimentacoes).HasForeignKey(m => m.EmpresaId);
            builder.HasOne(m => m.ItemEstoque).WithMany(i => i.Movimentacoes).HasForeignKey(m => m.ItemEstoqueId);
            builder.HasOne(m => m.Produto).WithMany(p => p.Movimentacoes).HasForeignKey(m => m.ProdutoId);
            builder.HasOne(m => m.ProdutoVariacao).WithMany(v => v.Movimentacoes).HasForeignKey(m => m.ProdutoVariacaoId).IsRequired(false);
            builder.HasOne(m => m.Venda).WithMany(v => v.Movimentacoes).HasForeignKey(m => m.VendaId).IsRequired(false);

            builder.Property(m => m.MovimentacaoEstornadaId);
            builder.Property(m => m.EstornadaEm);
            builder.HasOne(m => m.MovimentacaoEstornada).WithMany().HasForeignKey(m => m.MovimentacaoEstornadaId).IsRequired(false);
            builder.HasIndex(m => m.MovimentacaoEstornadaId).HasFilter("\"MovimentacaoEstornadaId\" IS NOT NULL");
        }
    }
}
