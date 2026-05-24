using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class CardapioItemConfiguration : IEntityTypeConfiguration<CardapioItem>
{
    public void Configure(EntityTypeBuilder<CardapioItem> builder)
    {
        builder.ToTable("cardapio_item");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.StorefrontId).IsRequired();
        builder.Property(c => c.ProdutoId).IsRequired();

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

        // Único (StorefrontId, ProdutoId): cada Produto aparece no máximo uma vez por storefront.
        builder.HasIndex(c => new { c.StorefrontId, c.ProdutoId })
            .IsUnique()
            .HasDatabaseName("uq_cardapio_item_storefront_produto");

        // Lookup por ordem dentro do storefront (lista pública do cardápio).
        builder.HasIndex(c => new { c.StorefrontId, c.OrdemExibicao })
            .HasDatabaseName("ix_cardapio_item_storefront_ordem");

        // FK Storefront — CASCADE: ao deletar storefront, remove todos os itens do cardápio.
        builder.HasOne<StorefrontEntity>()
            .WithMany()
            .HasForeignKey(c => c.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK Produto — RESTRICT: não permite deletar produto que está em algum cardápio.
        // Forces explicit cleanup: remover do cardápio antes de deletar produto.
        builder.HasOne(c => c.Produto)
            .WithMany()
            .HasForeignKey(c => c.ProdutoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
