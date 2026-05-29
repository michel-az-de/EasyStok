using EasyStock.Domain.Integration;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// EF mapping da tabela <c>credencial_integracao</c>. Persistência cifrada
/// (AES-256-GCM) por tenant + provider + ambiente. Filter multi-tenant
/// global (<see cref="EasyStockDbContext.ApplyTenantQueryFilters"/>) aplica
/// automaticamente filtro por <c>EmpresaId</c>.
/// </summary>
public class CredencialIntegracaoConfiguration : IEntityTypeConfiguration<CredencialIntegracao>
{
    public void Configure(EntityTypeBuilder<CredencialIntegracao> b)
    {
        b.ToTable("credencial_integracao");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(c => c.Categoria).HasColumnName("categoria").HasConversion<int>().IsRequired();
        b.Property(c => c.ProviderKey).HasColumnName("provider_key").HasMaxLength(60).IsRequired();
        b.Property(c => c.Ambiente).HasColumnName("ambiente").HasConversion<int>().IsRequired();

        // Payload + KEK + IV + Tag — cifrado AES-256-GCM
        b.Property(c => c.PayloadCifrado).HasColumnName("payload_cifrado").HasColumnType("bytea").IsRequired();
        b.Property(c => c.KekId).HasColumnName("kek_id").HasMaxLength(120).IsRequired();
        b.Property(c => c.Iv).HasColumnName("iv").HasColumnType("bytea").IsRequired();
        b.Property(c => c.Tag).HasColumnName("tag").HasColumnType("bytea").IsRequired();

        b.Property(c => c.ValidoDe).HasColumnName("valido_de").IsRequired();
        b.Property(c => c.ValidoAte).HasColumnName("valido_ate");
        b.Property(c => c.Ativo).HasColumnName("ativo").IsRequired();
        b.Property(c => c.UltimoUsoEm).HasColumnName("ultimo_uso_em");

        b.Property(c => c.CriadoPorUsuarioId).HasColumnName("criado_por_usuario_id").IsRequired();
        b.Property(c => c.CriadoEm).HasColumnName("criado_em").IsRequired();
        b.Property(c => c.AlteradoEm).HasColumnName("alterado_em").IsRequired();

        // Índice único filtrado: garante apenas UMA credencial ATIVA por tenant
        // + provider + ambiente. Credenciais inativas (rotação, histórico)
        // podem coexistir.
        b.HasIndex(c => new { c.EmpresaId, c.ProviderKey, c.Ambiente })
            .HasFilter("ativo = true")
            .IsUnique()
            .HasDatabaseName("ix_credencial_integracao_empresa_provider_ambiente_ativo");

        // Índice secundário pra rotação batch (encontrar todas com KEK antiga).
        b.HasIndex(c => c.KekId)
            .HasDatabaseName("ix_credencial_integracao_kek_id");

        b.HasOne(c => c.Empresa)
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
