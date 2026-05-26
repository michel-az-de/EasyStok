using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class CheckoutIdempotencyConfiguration : IEntityTypeConfiguration<CheckoutIdempotency>
{
    public void Configure(EntityTypeBuilder<CheckoutIdempotency> builder)
    {
        builder.ToTable("checkout_idempotency");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Key).IsRequired();

        builder.Property(c => c.ContentHash)
            .IsRequired()
            .HasMaxLength(64); // SHA-256 hex

        builder.Property(c => c.FaturaId);

        builder.Property(c => c.InitPoint)
            .HasMaxLength(500);

        builder.Property(c => c.CriadoEm).IsRequired();
        builder.Property(c => c.ExpiraEm).IsRequired();

        // Match (Key, ContentHash) único — semântica do registro:
        // mesma key+hash devolve a Fatura original; key+hash diferente é cart novo.
        builder.HasIndex(c => new { c.Key, c.ContentHash })
            .IsUnique()
            .HasDatabaseName("uq_checkout_idempotency_key_hash");

        // Lookup do cleanup job (remove expirados).
        builder.HasIndex(c => c.ExpiraEm)
            .HasDatabaseName("ix_checkout_idempotency_expira");
    }
}
