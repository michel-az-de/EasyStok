using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class SessaoChatSiteConfiguration : IEntityTypeConfiguration<SessaoChatSite>
{
    public void Configure(EntityTypeBuilder<SessaoChatSite> builder)
    {
        builder.ToTable("sessoes_chat_site");
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.ContatoIdExterno);

        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.StorefrontId).IsRequired();
        builder.Property(s => s.TokenHash).HasMaxLength(SessaoChatSite.TokenHashTamanho).IsRequired();
        builder.Property(s => s.CriadaEm).IsRequired();
        builder.Property(s => s.UltimoUsoEm).IsRequired();
        builder.Property(s => s.ExpiraEm).IsRequired();

        // Toda requisição do visitante busca a sessão pelo hash do token.
        builder.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName("uq_sessoes_chat_site_token_hash");
        // Limpeza das vencidas.
        builder.HasIndex(s => s.ExpiraEm).HasDatabaseName("ix_sessoes_chat_site_expira_em");

        builder.HasOne<Domain.Entities.Storefront.Storefront>()
            .WithMany()
            .HasForeignKey(s => s.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}