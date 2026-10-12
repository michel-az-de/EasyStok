namespace EasyStock.Infra.Postgre.Data.Configurations
{
    /// <summary>S24: tag do cliente, única por (ClienteId, Tag).</summary>
    public class ClienteTagConfiguration : IEntityTypeConfiguration<ClienteTag>
    {
        public void Configure(EntityTypeBuilder<ClienteTag> b)
        {
            b.ToTable("cliente_tags");
            b.HasKey(x => x.Id);
            // PK gerada no app: tag nova em Cliente.Tags rastreado vira INSERT, não UPDATE de 0 linhas (#1311, ADR-0028).
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.EmpresaId).IsRequired();
            b.Property(x => x.Tag).IsRequired().HasMaxLength(ClienteTag.TagTamanhoMaximo);
            b.Property(x => x.Origem).IsRequired().HasMaxLength(20);
            b.Property(x => x.CriadoEm).IsRequired();

            b.HasIndex(x => new { x.ClienteId, x.Tag })
                .IsUnique()
                .HasDatabaseName("uq_cliente_tags_cliente_tag");
            // Campanha por tag dentro da empresa.
            b.HasIndex(x => new { x.EmpresaId, x.Tag });

            b.ToTable(t => t.HasCheckConstraint(
                "ck_cliente_tags_origem",
                "\"Origem\" IN ('dona','agente','sistema')"));

            b.HasOne<Cliente>()
                .WithMany(c => c.Tags)
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>S24: nota interna do cliente. Nunca projetada para o cliente.</summary>
    public class ClienteNotaConfiguration : IEntityTypeConfiguration<ClienteNota>
    {
        public void Configure(EntityTypeBuilder<ClienteNota> b)
        {
            b.ToTable("cliente_notas");
            b.HasKey(x => x.Id);
            b.Property(x => x.EmpresaId).IsRequired();
            b.Property(x => x.Texto).IsRequired().HasMaxLength(ClienteNota.TextoTamanhoMaximo);
            b.Property(x => x.Autor).IsRequired().HasMaxLength(ClienteNota.AutorTamanhoMaximo);
            b.Property(x => x.CriadoEm).IsRequired();

            b.HasIndex(x => new { x.EmpresaId, x.ClienteId, x.CriadoEm });

            b.HasOne<Cliente>()
                .WithMany()
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
