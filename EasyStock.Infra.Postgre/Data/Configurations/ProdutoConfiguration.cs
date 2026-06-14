using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
    {
        public void Configure(EntityTypeBuilder<Produto> builder)
        {
            builder.ToTable("produtos");
            builder.HasKey(p => p.Id);

            // Optimistic concurrency via xmin (system column do Postgres,
            // sempre presente — não exige migration). EF compara versão
            // ao SaveChanges; se mudou no meio, lança DbUpdateConcurrencyException.
            builder.Property<uint>("xmin")
                .HasColumnName("xmin")
                .IsRowVersion();

            builder.Property(p => p.Nome).IsRequired().HasMaxLength(180);
            builder.Property(p => p.Marca).HasMaxLength(120);
            builder.Property(p => p.Tipo).HasConversion<string>().IsRequired().HasMaxLength(50);
            // Inserido 2026-05-16 (correcao C2 / RDC 727/2022). Persistido como string
            // (padrao do projeto - veja Tipo/Status acima). Default "Avulso" para
            // produtos existentes nao bloqueia comportamento atual ate triagem manual.
            builder.Property(p => p.TipoEmbalagem)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(EasyStock.Domain.Enums.TipoEmbalagem.Avulso);
            builder.Property(p => p.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(p => p.SkuBase)
                .HasConversion(
                    sku => sku == null ? null : sku.Value,
                    value => string.IsNullOrWhiteSpace(value) ? null : CodigoSku.From(value))
                .HasMaxLength(100);
            builder.Property(p => p.CodigoBarras).HasMaxLength(100);

            builder.OwnsOne(p => p.Dimensoes, dimensions =>
            {
                dimensions.Property(d => d.Peso).HasColumnName("peso").HasColumnType("decimal(10,3)");
                dimensions.Property(d => d.Largura).HasColumnName("largura").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Altura).HasColumnName("altura").HasColumnType("decimal(10,2)");
                dimensions.Property(d => d.Comprimento).HasColumnName("comprimento").HasColumnType("decimal(10,2)");
            });

            builder.Property(p => p.CustoReferencia)
                .HasConversion(
                    dinheiro => dinheiro == null ? (decimal?)null : dinheiro.Valor,
                    value => value.HasValue ? Dinheiro.FromDecimal(value.Value) : null)
                .HasColumnType("decimal(18,2)");
            builder.Property(p => p.PrecoReferencia)
                .HasConversion(
                    dinheiro => dinheiro == null ? (decimal?)null : dinheiro.Valor,
                    value => value.HasValue ? Dinheiro.FromDecimal(value.Value) : null)
                .HasColumnType("decimal(18,2)");
            builder.Property(p => p.MargemEstimada).HasColumnType("decimal(8,2)");

            // Receita / Calculadora de Producao (Onda 1.2)
            builder.Property(p => p.EhInsumo).IsRequired().HasDefaultValue(false);
            builder.Property(p => p.RendimentoBase).HasColumnType("numeric(19,4)").IsRequired().HasDefaultValue(1m);
            builder.Property(p => p.RendimentoUnidade).HasConversion<string>().HasMaxLength(8).IsRequired().HasDefaultValue(UnidadeMedida.Un);
            builder.Property(p => p.UnidadeMedidaBase).HasConversion<string>().HasMaxLength(8).IsRequired().HasDefaultValue(UnidadeMedida.Un);

            builder.Property(p => p.CriadoPor).HasColumnType("uuid");
            builder.Property(p => p.AlteradoPor).HasColumnType("uuid");
            builder.Property(p => p.ObservacaoInterna).HasMaxLength(1000).HasColumnType("character varying(1000)");

            builder.Property(p => p.AtributosJson).HasColumnType("jsonb");
            builder.Property(p => p.FotosJson).HasColumnType("jsonb");
            builder.Property(p => p.SugestaoDescricaoAnuncio).HasColumnType("text");

            builder.HasOne(p => p.Empresa).WithMany(e => e.Produtos).HasForeignKey(p => p.EmpresaId);
            builder.HasOne(p => p.Categoria).WithMany(c => c.Produtos).HasForeignKey(p => p.CategoriaId);
            builder.HasOne(p => p.Subcategoria).WithMany().HasForeignKey(p => p.SubcategoriaId).IsRequired(false);
            builder.HasMany(p => p.Caracteristicas).WithOne(c => c.Produto).HasForeignKey(c => c.ProdutoId);
            builder.HasMany(p => p.Embalagens).WithOne(e => e.Produto).HasForeignKey(e => e.ProdutoId);
            builder.HasMany(p => p.Variacoes).WithOne(v => v.Produto).HasForeignKey(v => v.ProdutoId);

            // #582 / ADR-0033: completude e DERIVADA (getters computados), nunca persistida.
            // Ignora explicitamente para o EF nao tentar mapear como coluna (model-build quebraria).
            builder.Ignore(p => p.CompletudePercent);
            builder.Ignore(p => p.Pendencias);

            // ── Indexes ──
            builder.HasIndex(p => new { p.EmpresaId, p.SkuBase })
                .IsUnique()
                .HasFilter("\"SkuBase\" IS NOT NULL")
                .HasDatabaseName("ix_produtos_empresa_sku_unique");

            builder.HasIndex(p => new { p.EmpresaId, p.Status })
                .HasDatabaseName("ix_produtos_empresa_status");

            builder.HasIndex(p => new { p.EmpresaId, p.AlteradoEm })
                .HasDatabaseName("ix_produtos_empresa_alteradoem");
        }
    }
}
