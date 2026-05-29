using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class ClienteSessionConfiguration : IEntityTypeConfiguration<ClienteSession>
{
    public void Configure(EntityTypeBuilder<ClienteSession> builder)
    {
        builder.ToTable("cliente_session");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.ClienteId).IsRequired();
        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.CriadoEm).IsRequired();
        builder.Property(s => s.UltimoUsoEm).IsRequired();

        builder.Property(s => s.IpInicial).HasMaxLength(45);
        builder.Property(s => s.UaInicial).HasMaxLength(300);
        builder.Property(s => s.Fingerprint).HasMaxLength(64);

        builder.Property(s => s.Revogada).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.MotivoRevogacao).HasMaxLength(200);

        // FK Cliente — CASCADE: anonimização/deleção de cliente revoga sessions.
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(s => s.ClienteId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK Empresa — CASCADE (multi-tenant; remoção da empresa limpa tudo).
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(s => s.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lookup principal do middleware: "esta session ainda vale?" — sid → row.
        // PK Id já cobre. Indices abaixo cobrem cleanup e logout-all.

        // logout-all do cliente: query por ClienteId + Revogada=false.
        builder.HasIndex(s => new { s.ClienteId, s.Revogada })
            .HasDatabaseName("ix_cliente_session_cliente_revogada");

        // Job de limpeza periódica (sessions inativas > 90d): UltimoUsoEm.
        builder.HasIndex(s => s.UltimoUsoEm)
            .HasDatabaseName("ix_cliente_session_ultimo_uso");
    }
}
