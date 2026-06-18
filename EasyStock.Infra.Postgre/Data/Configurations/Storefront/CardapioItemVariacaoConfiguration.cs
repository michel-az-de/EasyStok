using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

/// <summary>
/// Mapeamento de <see cref="CardapioItemVariacao"/> (ADR-0035 / épico #645).
///
/// <para>
/// A unicidade case-insensitive de <c>Rotulo</c> por item NÃO é declarada aqui: é feita na migration
/// via coluna gerada <c>rotulo_lower</c> + UNIQUE CONSTRAINT DEFERRABLE (raw SQL). Índice de expressão
/// não é deferível no Postgres, e o índice deferível precisa ser de coluna/constraint — por isso fica
/// na migration. O EhPadrao também não tem índice de banco (invariante de agregado).
/// </para>
/// </summary>
public class CardapioItemVariacaoConfiguration : IEntityTypeConfiguration<CardapioItemVariacao>
{
    public void Configure(EntityTypeBuilder<CardapioItemVariacao> builder)
    {
        builder.ToTable("cardapio_item_variacao");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.CardapioItemId).IsRequired();

        builder.Property(v => v.Rotulo)
            .IsRequired()
            .HasMaxLength(60);

        builder.Property(v => v.PrecoStorefront)
            .HasColumnType("decimal(10,2)");

        // CodigoSku ↔ string (mesmo padrão de ProdutoVariacaoConfiguration).
        builder.Property(v => v.Sku)
            .HasConversion(
                sku => sku == null ? null : sku.Value,
                value => string.IsNullOrWhiteSpace(value) ? null : CodigoSku.From(value))
            .HasMaxLength(100);

        builder.Property(v => v.PesoExibicao).HasMaxLength(50);

        builder.Property(v => v.Disponivel)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(v => v.OrdemExibicao)
            .IsRequired()
            .HasDefaultValue(0d);

        builder.Property(v => v.EhPadrao)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(v => v.CriadoEm).IsRequired();
        builder.Property(v => v.AlteradoEm).IsRequired();

        // FK CardapioItem — CASCADE: apagar o item apaga as opções.
        builder.HasOne(v => v.CardapioItem)
            .WithMany(c => c.Variacoes)
            .HasForeignKey(v => v.CardapioItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK ProdutoVariacao — RESTRICT + opcional: não apagar variação do ERP em uso no cardápio.
        builder.HasOne(v => v.ProdutoVariacao)
            .WithMany()
            .HasForeignKey(v => v.ProdutoVariacaoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Carregar/ordenar opções de um item.
        builder.HasIndex(v => new { v.CardapioItemId, v.OrdemExibicao })
            .HasDatabaseName("ix_cardapio_item_variacao_item_ordem");
    }
}
