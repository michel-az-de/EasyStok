using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class LembreteConfiguration : IEntityTypeConfiguration<Lembrete>
{
    public void Configure(EntityTypeBuilder<Lembrete> builder)
    {
        builder.ToTable("lembretes");
        builder.HasKey(l => l.Id);

        builder.Ignore(l => l.EstaAberto);
        builder.Property(l => l.EmpresaId).IsRequired();
        builder.Property(l => l.Tipo).HasConversion<int>().IsRequired();
        builder.Property(l => l.Situacao).HasConversion<int>().IsRequired();
        builder.Property(l => l.Referencia).HasMaxLength(Lembrete.ReferenciaTamanhoMaximo);
        builder.Property(l => l.Texto).HasMaxLength(Lembrete.TextoTamanhoMaximo).IsRequired();
        builder.Property(l => l.VenceEm).IsRequired();
        builder.Property(l => l.CriadoEm).IsRequired();

        // Idempotência do avaliador (S43): um lembrete por fato. O manual não tem referência.
        builder.HasIndex(l => new { l.EmpresaId, l.Tipo, l.Referencia })
            .IsUnique()
            .HasFilter("\"Referencia\" IS NOT NULL")
            .HasDatabaseName("ux_lembretes_empresa_tipo_referencia");
        // Sininho e avaliador: abertos por vencimento.
        builder.HasIndex(l => new { l.EmpresaId, l.Situacao, l.VenceEm })
            .HasDatabaseName("ix_lembretes_empresa_situacao_vence_em");

        // Lembrete é anotação: some a conversa ou o pedido, o lembrete fica sem o vínculo.
        builder.HasOne<Conversa>().WithMany().HasForeignKey(l => l.ConversaId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Domain.Entities.Pedido>().WithMany().HasForeignKey(l => l.PedidoId).OnDelete(DeleteBehavior.SetNull);
    }
}
