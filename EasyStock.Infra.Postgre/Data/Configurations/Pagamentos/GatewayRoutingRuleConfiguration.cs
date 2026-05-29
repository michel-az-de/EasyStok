using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Infra.Postgre.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Pagamentos;

public class GatewayRoutingRuleConfiguration : IEntityTypeConfiguration<GatewayRoutingRule>
{
    public void Configure(EntityTypeBuilder<GatewayRoutingRule> b)
    {
        b.ToTable("gateway_routing_rules");
        b.HasKey(r => r.Id);

        // EmpresaId nullable — NULL = regra global. Tipo isento do Global Query
        // Filter (igual a TenantFeatureFlag). Repository filtra manualmente
        // EmpresaId == tenant OR EmpresaId IS NULL.
        b.Property(r => r.EmpresaId);
        b.Property(r => r.Metodo).IsRequired().HasMaxLength(20);
        b.Property(r => r.Provedor).IsRequired().HasMaxLength(40);
        b.Property(r => r.Prioridade).IsRequired();
        b.Property(r => r.Ativo).IsRequired().HasDefaultValue(true);
        b.Property(r => r.Moeda).IsRequired().HasMaxLength(3).HasDefaultValue("BRL");
        b.Property(r => r.Pais).IsRequired().HasMaxLength(2).HasDefaultValue("BR");
        b.Property(r => r.RegrasJson).HasColumnType("jsonb");
        b.ConfigureCriadoEm(r => r.CriadoEm)
         .ConfigureAtualizadoEm(r => r.AtualizadoEm);

        // RowVersion via xmin (Postgres system column)
        b.Property(r => r.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Indices

        // Query principal do router: filtra por (EmpresaId, Metodo, Ativo) e ordena por Prioridade.
        b.HasIndex(r => new { r.EmpresaId, r.Metodo, r.Ativo, r.Prioridade })
            .HasDatabaseName("ix_gateway_routing_rules_empresa_metodo_ativo_prioridade");

        // Uma regra por combinacao (EmpresaId NULLS NOT DISTINCT, Metodo, Provedor, Moeda, Pais).
        // Garante que nao existem 2 regras para o mesmo (tenant ou global, metodo, provedor).
        b.HasIndex(r => new { r.EmpresaId, r.Metodo, r.Provedor, r.Moeda, r.Pais })
            .HasDatabaseName("ux_gateway_routing_rules_empresa_metodo_provedor_moeda_pais")
            .IsUnique();
    }
}
