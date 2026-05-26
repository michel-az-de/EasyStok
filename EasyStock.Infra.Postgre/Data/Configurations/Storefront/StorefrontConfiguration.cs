using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class StorefrontConfiguration : IEntityTypeConfiguration<StorefrontEntity>
{
    public void Configure(EntityTypeBuilder<StorefrontEntity> builder)
    {
        builder.ToTable("storefront");
        builder.HasKey(s => s.Id);

        // Multi-tenant discriminator
        builder.Property(s => s.EmpresaId)
            .IsRequired();
        builder.HasIndex(s => s.EmpresaId)
            .HasDatabaseName("ix_storefront_empresa_id");

        // Slug — único globalmente (lookup público por slug)
        builder.Property(s => s.Slug)
            .IsRequired()
            .HasMaxLength(40);
        builder.HasIndex(s => s.Slug)
            .IsUnique()
            .HasDatabaseName("uq_storefront_slug");

        // Domínio custom — único quando presente
        builder.Property(s => s.DominioCustom)
            .HasMaxLength(100);
        builder.HasIndex(s => s.DominioCustom)
            .IsUnique()
            .HasFilter("\"DominioCustom\" IS NOT NULL")
            .HasDatabaseName("uq_storefront_dominio_custom");

        builder.Property(s => s.TituloPublico)
            .IsRequired()
            .HasMaxLength(120);
        builder.Property(s => s.SubtituloPublico).HasMaxLength(240);
        builder.Property(s => s.LogoUrl).HasMaxLength(500);
        builder.Property(s => s.CorPrimaria).HasMaxLength(7);
        builder.Property(s => s.WhatsappPedidos).HasMaxLength(20);
        builder.Property(s => s.MensagemForaArea).HasMaxLength(500);

        builder.Property(s => s.PedidoMinimoEntrega)
            .HasColumnType("decimal(10,2)")
            .IsRequired();
        builder.Property(s => s.FreteGratisAcima)
            .HasColumnType("decimal(10,2)");

        // Feature flags fiscais — defaults safe (ADR-0010)
        builder.Property(s => s.NfeAutomaticaHabilitada)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(s => s.ModeloFiscal)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue("manual");

        builder.Property(s => s.Ativo)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.CriadoEm).IsRequired();
        builder.Property(s => s.AlteradoEm).IsRequired();

        // FK opcional para Loja padrão
        builder.HasOne<EasyStock.Domain.Entities.Loja>()
            .WithMany()
            .HasForeignKey(s => s.LojaPadraoId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK obrigatória para Empresa
        builder.HasOne<EasyStock.Domain.Entities.Empresa>()
            .WithMany()
            .HasForeignKey(s => s.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
