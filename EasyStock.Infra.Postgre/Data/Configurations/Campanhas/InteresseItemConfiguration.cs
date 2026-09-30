using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Campanhas;

public class InteresseItemConfiguration : IEntityTypeConfiguration<InteresseItem>
{
    public void Configure(EntityTypeBuilder<InteresseItem> builder)
    {
        builder.ToTable("interesses_item");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.EmpresaId).IsRequired();
        builder.Property(i => i.ClienteId).IsRequired();
        builder.Property(i => i.Descricao).HasMaxLength(InteresseItem.DescricaoTamanhoMaximo);
        builder.Property(i => i.Origem).IsRequired().HasMaxLength(InteresseItem.OrigemTamanhoMaximo);
        builder.Property(i => i.RegistradoEm).IsRequired();
        builder.Ignore(i => i.Aberto);

        // Sugestão e contagem por item (S31): só os abertos interessam.
        builder.HasIndex(i => new { i.EmpresaId, i.CardapioItemId })
            .HasFilter("\"AtendidoEm\" IS NULL")
            .HasDatabaseName("ix_interesses_item_abertos_por_item");

        builder.HasOne<Domain.Entities.Cliente>()
            .WithMany()
            .HasForeignKey(i => i.ClienteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Item removido do cardápio não apaga o interesse: fica a descrição (o nome do item).
        builder.HasOne<CardapioItem>()
            .WithMany()
            .HasForeignKey(i => i.CardapioItemId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
