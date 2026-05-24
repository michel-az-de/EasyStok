using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class BloqueioEntregaConfiguration : IEntityTypeConfiguration<BloqueioEntrega>
{
    public void Configure(EntityTypeBuilder<BloqueioEntrega> builder)
    {
        builder.ToTable("bloqueio_entrega");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.StorefrontId).IsRequired();
        builder.Property(b => b.Data).IsRequired();
        builder.Property(b => b.JanelaEspecificaId);

        builder.Property(b => b.Motivo)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(b => b.CriadoEm).IsRequired();

        // Lookup por storefront + data (ListarJanelasDisponiveis filtra bloqueios em janela de 14 dias).
        builder.HasIndex(b => new { b.StorefrontId, b.Data })
            .HasDatabaseName("ix_bloqueio_entrega_storefront_data");

        // FK Storefront — CASCADE.
        builder.HasOne<StorefrontEntity>()
            .WithMany()
            .HasForeignKey(b => b.StorefrontId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK opcional para JanelaEntrega — RESTRICT: se há bloqueio apontando para janela,
        // forçar limpeza explícita antes de deletar a janela.
        builder.HasOne<JanelaEntrega>()
            .WithMany()
            .HasForeignKey(b => b.JanelaEspecificaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
