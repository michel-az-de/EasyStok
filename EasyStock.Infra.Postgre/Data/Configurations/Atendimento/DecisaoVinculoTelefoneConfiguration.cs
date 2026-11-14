using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

/// <summary>#2492. Sem FK para clientes: o cadastro excluído não apaga a decisão; o lookup ignora a escolha.</summary>
public sealed class DecisaoVinculoTelefoneConfiguration : IEntityTypeConfiguration<DecisaoVinculoTelefone>
{
    public void Configure(EntityTypeBuilder<DecisaoVinculoTelefone> builder)
    {
        builder.ToTable("decisoes_vinculo_telefone");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TelefoneHash).HasMaxLength(64).IsRequired();
        builder.Ignore(x => x.Ativa);
        builder.HasIndex(x => new { x.EmpresaId, x.TelefoneHash }).IsUnique().HasFilter("\"RevogadaEm\" IS NULL");
        builder.HasOne<Conversa>().WithMany().HasForeignKey(x => x.ConversaOrigemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlteracaoVinculoConversa>().WithMany().HasForeignKey(x => x.AlteracaoVinculoId).OnDelete(DeleteBehavior.Restrict);
    }
}
