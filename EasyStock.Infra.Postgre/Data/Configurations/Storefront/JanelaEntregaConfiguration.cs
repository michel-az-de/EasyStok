using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class JanelaEntregaConfiguration : IEntityTypeConfiguration<JanelaEntrega>
{
    public void Configure(EntityTypeBuilder<JanelaEntrega> builder)
    {
        builder.ToTable("janela_entrega");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.StorefrontId).IsRequired();
        builder.Property(j => j.DiaDaSemana).IsRequired();
        builder.Property(j => j.HoraInicio).IsRequired();
        builder.Property(j => j.HoraFim).IsRequired();
        builder.Property(j => j.CapacidadeMaxima).IsRequired();

        builder.Property(j => j.Label)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(j => j.Ativa)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(j => j.CriadoEm).IsRequired();
        builder.Property(j => j.AlteradoEm).IsRequired();

        // Lookup por storefront + dia (ListarJanelasDisponiveisUseCase varre 14 dias).
        builder.HasIndex(j => new { j.StorefrontId, j.DiaDaSemana })
            .HasDatabaseName("ix_janela_entrega_storefront_dia");

        // FK Storefront — CASCADE: ao deletar storefront, remove janelas.
        builder.HasOne<StorefrontEntity>()
            .WithMany()
            .HasForeignKey(j => j.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
