using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public sealed class AlteracaoVinculoConversaConfiguration : IEntityTypeConfiguration<AlteracaoVinculoConversa>
{
    public void Configure(EntityTypeBuilder<AlteracaoVinculoConversa> builder)
    {
        builder.ToTable("alteracoes_vinculo_conversa");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Acao).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Motivo).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.EmpresaId, x.ConversaId, x.RequestId }).IsUnique();
        builder.HasIndex(x => new { x.EmpresaId, x.ConversaId, x.Revisao }).IsUnique();
        builder.HasOne<Conversa>().WithMany().HasForeignKey(x => x.ConversaId).OnDelete(DeleteBehavior.Restrict);
    }
}
