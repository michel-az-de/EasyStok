using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class StorefrontFaleConoscoConfiguration : IEntityTypeConfiguration<StorefrontFaleConosco>
{
    public void Configure(EntityTypeBuilder<StorefrontFaleConosco> builder)
    {
        builder.ToTable("storefront_fale_conosco");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.StorefrontId).IsRequired();
        builder.Property(f => f.ClienteId);

        builder.Property(f => f.Nome)
            .IsRequired()
            .HasMaxLength(120);
        builder.Property(f => f.Telefone)
            .IsRequired()
            .HasMaxLength(30);
        builder.Property(f => f.Email).HasMaxLength(254);

        builder.Property(f => f.Assunto)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(f => f.Mensagem)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(f => f.CriadoEm).IsRequired();

        builder.Property(f => f.Respondido)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(f => f.RespondidoPor).HasMaxLength(120);
        builder.Property(f => f.RespondidoEm);

        // Triagem do inbox: pendentes primeiro, ordenadas por chegada.
        builder.HasIndex(f => new { f.StorefrontId, f.Respondido, f.CriadoEm })
            .HasDatabaseName("ix_fale_conosco_storefront_resp_data");

        // FK Cliente opcional — SetNull: preserva mensagem se cliente for removido.
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(f => f.ClienteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
