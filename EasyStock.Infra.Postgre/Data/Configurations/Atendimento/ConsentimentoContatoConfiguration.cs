using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class ConsentimentoContatoConfiguration : IEntityTypeConfiguration<ConsentimentoContato>
{
    public void Configure(EntityTypeBuilder<ConsentimentoContato> builder)
    {
        builder.ToTable("consentimentos_contato");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.ClienteId).IsRequired();
        builder.Property(c => c.Canal).HasConversion<int>().IsRequired();
        builder.Property(c => c.Finalidade).HasConversion<int>().IsRequired();
        builder.Property(c => c.Situacao).HasConversion<int>().IsRequired();
        builder.Property(c => c.Origem).IsRequired().HasMaxLength(ConsentimentoContato.OrigemTamanhoMaximo);
        builder.Property(c => c.AtualizadoEm).IsRequired();

        // Uma linha atual por cliente, canal e finalidade (S38): alteração é no lugar.
        builder.HasIndex(c => new { c.EmpresaId, c.ClienteId, c.Canal, c.Finalidade })
            .IsUnique()
            .HasDatabaseName("uq_consentimentos_contato_cliente_canal_finalidade");

        builder.HasOne<Domain.Entities.Cliente>()
            .WithMany()
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
