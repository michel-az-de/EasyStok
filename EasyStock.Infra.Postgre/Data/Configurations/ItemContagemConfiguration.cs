using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ItemContagemConfiguration : IEntityTypeConfiguration<ItemContagem>
    {
        public void Configure(EntityTypeBuilder<ItemContagem> builder)
        {
            builder.ToTable("itens_contagem");
            builder.HasKey(i => i.Id);

            // xmin: concorrencia otimista (edge 2-operadores no mesmo lote; 409 no PATCH).
            // Espelha ItemEstoqueConfiguration; xmin e coluna de sistema do Postgres,
            // nao gera coluna na migration.
            builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

            builder.Property(i => i.QtdSistemaNoMomento)
                .HasConversion(
                    q => q == null ? (decimal?)null : q.Value,
                    value => value.HasValue ? Quantidade.From(value.Value) : null)
                .HasColumnType("numeric(18,3)");
            builder.Property(i => i.QtdContada)
                .HasConversion(
                    q => q == null ? (decimal?)null : q.Value,
                    value => value.HasValue ? Quantidade.From(value.Value) : null)
                .HasColumnType("numeric(18,3)");
            builder.Property(i => i.CustoUnitarioSnapshot)
                .HasConversion(
                    d => d == null ? (decimal?)null : d.Valor,
                    value => value.HasValue ? Dinheiro.FromDecimal(value.Value) : null)
                .HasColumnType("decimal(18,2)");
            builder.Property(i => i.Validade)
                .HasConversion(
                    v => v == null ? (DateTime?)null : v.DataValidade,
                    value => value.HasValue ? Validade.From(value.Value) : null);

            // Divergencia derivada (contado - sistema), nunca persistida.
            builder.Ignore(i => i.Divergencia);

            builder.HasOne<Empresa>().WithMany()
                .HasForeignKey(i => i.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            // Contagem -> Itens (Cascade) configurado em ContagemConfiguration.
            builder.HasOne(i => i.Produto).WithMany()
                .HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(i => i.ItemEstoque).WithMany()
                .HasForeignKey(i => i.ItemEstoqueId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(i => i.ContagemId).HasDatabaseName("ix_itens_contagem_contagem");
        }
    }
}
