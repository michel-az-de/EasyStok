namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// F10-C-3 — Configuracao da tabela <c>mobile_processed_mutations</c>.
/// A PK composta (MutationId, DeviceId) NAO garantia a idempotencia GLOBAL que o
/// SyncController assume (o pre-check dedupa por MutationId sozinho): dois pushes da
/// mesma mutation com DeviceIds diferentes (re-pareamento) passavam ambos e aplicavam
/// a mutation 2x (venda/estoque duplicados). O indice unico em MutationId (#789)
/// fecha isso no banco.
/// </summary>
public class MobileProcessedMutationConfiguration : IEntityTypeConfiguration<MobileProcessedMutation>
{
    public void Configure(EntityTypeBuilder<MobileProcessedMutation> builder)
    {
        builder.ToTable("mobile_processed_mutations");

        builder.HasKey(m => new { m.MutationId, m.DeviceId });

        builder.Property(m => m.MutationId)
            .HasMaxLength(60)
            .HasColumnType("character varying(60)")
            .IsRequired();

        builder.Property(m => m.DeviceId)
            .HasMaxLength(60)
            .HasColumnType("character varying(60)")
            .IsRequired();

        builder.Property(m => m.EmpresaId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(m => m.Outcome)
            .HasMaxLength(30)
            .HasColumnType("character varying(30)")
            .IsRequired();

        builder.Property(m => m.ResponseMeta)
            .HasColumnType("text");

        builder.Property(m => m.CriadoEm)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Retention cleanup index
        builder.HasIndex(m => new { m.EmpresaId, m.CriadoEm })
            .HasDatabaseName("ix_mpm_retention");

        // Idempotencia GLOBAL por MutationId (#789): o pre-check do SyncController dedupa
        // por MutationId sozinho, mas a PK composta permitia a mesma mutation com DeviceIds
        // diferentes. Indice unico garante o invariante no banco.
        builder.HasIndex(m => m.MutationId)
            .IsUnique()
            .HasDatabaseName("ux_mpm_mutation_id");
    }
}
