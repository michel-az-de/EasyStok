using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class CardapioItemConfiguration : IEntityTypeConfiguration<CardapioItem>
{
    public void Configure(EntityTypeBuilder<CardapioItem> builder)
    {
        builder.ToTable("cardapio_item");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.StorefrontId).IsRequired();

        // ProdutoId nullable: null = item avulso (sem vínculo com ERP).
        // CHECK constraint no banco: produto_id IS NOT NULL OR nome_publico IS NOT NULL
        // (adicionada via migration 0031-cardapio-produto-agnostico).
        builder.Property(c => c.ProdutoId).IsRequired(false);

        // Novos campos (ADR-0031): presentes no banco após migration 0031.
        builder.Property(c => c.NomePublico).HasMaxLength(200);
        builder.Property(c => c.CategoriaTexto).HasMaxLength(100);

        builder.Property(c => c.Visivel)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(c => c.Disponivel)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.OrdemExibicao)
            .IsRequired()
            .HasDefaultValue(0d);

        builder.Property(c => c.DescricaoPublica).HasMaxLength(240);
        builder.Property(c => c.Ingredientes).HasMaxLength(500);
        builder.Property(c => c.Alergenos).HasMaxLength(200);
        builder.Property(c => c.SugestaoMolho).HasMaxLength(200);
        builder.Property(c => c.TempoPreparo).HasMaxLength(50);
        builder.Property(c => c.FotoUrl).HasMaxLength(500);
        builder.Property(c => c.PesoExibicao).HasMaxLength(50);

        builder.Property(c => c.PrecoStorefront)
            .HasColumnType("decimal(10,2)");

        builder.Property(c => c.Tag).HasMaxLength(20);

        builder.Property(c => c.FiltrosJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValue("[]");

        builder.Property(c => c.CriadoEm).IsRequired();
        builder.Property(c => c.AlteradoEm).IsRequired();

        // Único (StorefrontId, ProdutoId) para itens VINCULADOS: cada Produto aparece
        // no máximo uma vez por storefront. A condição WHERE produto_id IS NOT NULL
        // é aplicada via SQL bruto na migration (EF não suporta partial index nativo).
        builder.HasIndex(c => new { c.StorefrontId, c.ProdutoId })
            .IsUnique()
            .HasFilter("\"ProdutoId\" IS NOT NULL")
            .HasDatabaseName("uq_cardapio_item_storefront_produto");
        // Nota: índice único para avulso (LOWER(nome_publico)) WHERE produto_id IS NULL
        // é criado via migrationBuilder.Sql(..., suppressTransaction: true) na migration 0031.

        // Lookup por ordem dentro do storefront (lista pública do cardápio).
        builder.HasIndex(c => new { c.StorefrontId, c.OrdemExibicao })
            .HasDatabaseName("ix_cardapio_item_storefront_ordem");

        // FK Storefront — CASCADE: ao deletar storefront, remove todos os itens do cardápio.
        builder.HasOne<StorefrontEntity>()
            .WithMany()
            .HasForeignKey(c => c.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK Produto — RESTRICT: não permite deletar produto que está em algum cardápio.
        // IsRequired(false): FK é opcional — itens avulsos têm ProdutoId = null.
        builder.HasOne(c => c.Produto)
            .WithMany()
            .HasForeignKey(c => c.ProdutoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
