using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class LinkCardapioConversaConfiguration : IEntityTypeConfiguration<LinkCardapioConversa>
{
    public void Configure(EntityTypeBuilder<LinkCardapioConversa> builder)
    {
        builder.ToTable("links_cardapio_conversa");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.EmpresaId).IsRequired();
        builder.Property(l => l.ConversaId).IsRequired();
        builder.Property(l => l.TokenHash).HasMaxLength(LinkCardapioConversa.TokenHashTamanho).IsRequired();
        builder.Property(l => l.CriadoEm).IsRequired();
        builder.Property(l => l.ExpiraEm).IsRequired();

        // O site chama a API só com o token: a busca é pelo hash.
        builder.HasIndex(l => l.TokenHash).IsUnique().HasDatabaseName("uq_links_cardapio_conversa_token_hash");

        builder.HasOne<Conversa>()
            .WithMany()
            .HasForeignKey(l => l.ConversaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
