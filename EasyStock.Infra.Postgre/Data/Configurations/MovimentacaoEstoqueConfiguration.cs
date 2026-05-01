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
                .HasConversion(
                    q => q == null ? 0 : q.Value,
                    value => value >= 0 ? Quantidade.From(value) : Quantidade.Zero);
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

            // Auditoria: quem/de onde/em que dispositivo a movimentacao foi gerada.
            builder.Property(m => m.UsuarioId);
            builder.Property(m => m.Ip).HasMaxLength(64);
            builder.Property(m => m.UserAgent).HasMaxLength(500);
            builder.Property(m => m.DispositivoId).HasMaxLength(120);
            builder.Property(m => m.MotivoEstorno).HasMaxLength(300);

            // Índices para queries de KPI, analytics e filtros mais comuns
            builder.HasIndex(m => new { m.EmpresaId, m.Tipo, m.DataMovimentacao })
                .HasDatabaseName("ix_movimentacoes_empresa_tipo_data");
            builder.HasIndex(m => new { m.EmpresaId, m.Natureza })
                .HasDatabaseName("ix_movimentacoes_empresa_natureza");
            builder.HasIndex(m => new { m.EmpresaId, m.UsuarioId, m.DataMovimentacao })
                .HasDatabaseName("ix_movimentacoes_empresa_usuario_data");
        }
    }
}
