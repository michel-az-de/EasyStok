using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

/// <summary>
/// Mapeamento de <see cref="CardapioSecao"/> (ADR-0035 / épico #645).
/// Seção hierárquica do cardápio (≤3 níveis), dona pelo Storefront, sem reparent na v1.
/// </summary>
public class CardapioSecaoConfiguration : IEntityTypeConfiguration<CardapioSecao>
{
    public void Configure(EntityTypeBuilder<CardapioSecao> builder)
    {
        builder.ToTable("cardapio_secao", t =>
            t.HasCheckConstraint("ck_cardapio_secao_nivel", "\"Nivel\" BETWEEN 0 AND 2"));

        builder.HasKey(s => s.Id);

        builder.Property(s => s.StorefrontId).IsRequired();

        builder.Property(s => s.Nome)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.OrdemExibicao)
            .IsRequired()
            .HasDefaultValue(0d);

        builder.Property(s => s.Visivel)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.Nivel)
            .IsRequired()
            .HasColumnType("smallint");

        builder.Property(s => s.CriadoEm).IsRequired();
        builder.Property(s => s.AlteradoEm).IsRequired();

        // FK Storefront — CASCADE: apagar storefront remove suas seções.
        builder.HasOne<StorefrontEntity>()
            .WithMany()
            .HasForeignKey(s => s.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-ref — RESTRICT: bloqueia apagar seção com filhas (23503 → mensagem amigável na Application).
        builder.HasOne(s => s.SecaoPai)
            .WithMany(s => s.SubSecoes)
            .HasForeignKey(s => s.SecaoPaiId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Caminhar a árvore por storefront/pai/ordem.
        builder.HasIndex(s => new { s.StorefrontId, s.SecaoPaiId, s.OrdemExibicao })
            .HasDatabaseName("ix_cardapio_secao_storefront_pai_ordem");
    }
}
