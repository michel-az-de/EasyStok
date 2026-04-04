using EasyStok.Domain.Entities;
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
            builder.Property(m => m.Tipo).IsRequired().HasMaxLength(50);
            builder.Property(m => m.Natureza).IsRequired().HasMaxLength(50);
            builder.Property(m => m.ValorUnitario).HasColumnType("decimal(18,2)");
            builder.Property(m => m.ValorTotal).HasColumnType("decimal(18,2)");

            builder.HasOne(m => m.Empresa).WithMany(e => e.Movimentacoes).HasForeignKey(m => m.EmpresaId);
            builder.HasOne(m => m.ItemEstoque).WithMany(i => i.Movimentacoes).HasForeignKey(m => m.ItemEstoqueId);
            builder.HasOne(m => m.Produto).WithMany(p => p.Movimentacoes).HasForeignKey(m => m.ProdutoId);
            builder.HasOne(m => m.Venda).WithMany(v => v.Movimentacoes).HasForeignKey(m => m.VendaId).IsRequired(false);
        }
    }
}
