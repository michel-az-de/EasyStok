using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ItemEstoqueConfiguration : IEntityTypeConfiguration<ItemEstoque>
    {
        public void Configure(EntityTypeBuilder<ItemEstoque> builder)
        {
            builder.ToTable("itens_estoque");
            builder.HasKey(i => i.Id);
            builder.Property<uint>("xmin")
                .HasColumnName("xmin")
                .IsRowVersion();
            builder.Property(i => i.CodigoInterno).HasMaxLength(120);
            builder.Property(i => i.CodigoLote)
                .HasConversion(
                    lote => lote == null ? null : lote.Value,
                    value => string.IsNullOrWhiteSpace(value) ? null : CodigoLote.From(value))
                .HasMaxLength(120);
            builder.Property(i => i.CodigoMarketplace).HasMaxLength(120);
            builder.Property(i => i.ChavePesquisa).HasMaxLength(300);
            builder.Property(i => i.VariacaoDescricao).HasMaxLength(180);
            builder.Property(i => i.Cor).HasMaxLength(60);
            builder.Property(i => i.Tamanho).HasMaxLength(60);
            builder.Property(i => i.DescricaoAnuncio).HasColumnType("text");
            builder.Property(i => i.Status).HasConversion<string>().IsRequired().HasMaxLength(50);

            builder.OwnsOne(i => i.DimensoesReais, dimensions =>
            {
                dimensions.Property(d => d.Peso).HasColumnName("peso_real").HasColumnType("decimal(10,3)");
                dimensions.Property(d => d.Largura).HasColumnName("largura_real").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Altura).HasColumnName("altura_real").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Comprimento).HasColumnName("comprimento_real").HasColumnType("decimal(10,2)");
            });

            builder.Property(i => i.QuantidadeInicial)
                .HasConversion(
                    q => q == null ? 0m : q.Value,
                    value => value >= 0 ? Quantidade.From(value) : Quantidade.Zero)
                .HasColumnType("numeric(18,3)");
            builder.Property(i => i.QuantidadeAtual)
                .HasConversion(
                    q => q == null ? 0m : q.Value,
                    value => value >= 0 ? Quantidade.From(value) : Quantidade.Zero)
                .HasColumnType("numeric(18,3)");
            builder.Property(i => i.QuantidadeMinima)
                .HasDefaultValue(5);
            builder.Property(i => i.QuantidadeCritica)
                .HasDefaultValue(2);
            builder.Property(i => i.VelocidadeSaidaDiaria)
                .HasColumnType("decimal(10,2)");
            builder.Property(i => i.DiasSemMovimentacao);
            builder.Property(i => i.PrevisaoZeramentoDias);
            builder.Property(i => i.CustoUnitario)
                .HasConversion(
                    d => d == null ? 0m : d.Valor,
                    value => value >= 0 ? Dinheiro.FromDecimal(value) : Dinheiro.Zero)
                .HasColumnType("decimal(18,2)");
            builder.Property(i => i.PrecoVendaSugerido)
                .HasConversion(
                    d => d == null ? (decimal?)null : d.Valor,
                    value => value.HasValue ? Dinheiro.FromDecimal(value.Value) : null)
                .HasColumnType("decimal(18,2)");
            builder.Property(i => i.ValidadeEm)
                .HasConversion(
                    validade => validade == null ? (DateTime?)null : validade.DataValidade,
                    value => value.HasValue ? Validade.From(value.Value) : null);

            builder.HasOne(i => i.Empresa).WithMany(e => e.ItensEstoque).HasForeignKey(i => i.EmpresaId);
            builder.HasOne(i => i.Produto).WithMany(p => p.ItensEstoque).HasForeignKey(i => i.ProdutoId);
            builder.HasOne(i => i.ProdutoVariacao).WithMany(v => v.ItensEstoque).HasForeignKey(i => i.ProdutoVariacaoId).IsRequired(false);

            builder.Property(x => x.LojaId).HasColumnType("uuid");
            builder.Property(x => x.FornecedorId).HasColumnType("uuid");
            builder.HasOne(x => x.Loja).WithMany(l => l.Itens).HasForeignKey(x => x.LojaId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            builder.HasOne(x => x.Fornecedor).WithMany().HasForeignKey(x => x.FornecedorId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);

            // Índices para queries de estoque baixo, vencimento e itens parados
            builder.HasIndex(i => new { i.EmpresaId, i.QuantidadeAtual })
                .HasDatabaseName("ix_itens_estoque_empresa_quantidade");
            builder.HasIndex(i => new { i.EmpresaId, i.UltimaMovimentacaoEm })
                .HasDatabaseName("ix_itens_estoque_empresa_ultima_mov");
            builder.HasIndex(i => new { i.EmpresaId, i.ValidadeEm })
                .HasDatabaseName("ix_itens_estoque_empresa_validade");
        }
    }
}
