namespace EasyStock.Infra.Postgre.Data.Configurations;

public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).IsRequired().HasMaxLength(120);
        builder.Property(x => x.MetodoRecurso).IsRequired().HasMaxLength(200);
        builder.Property(x => x.HttpStatus).IsRequired();
        // Resposta truncada via JsonResposta — evitamos limite explicito pra
        // cobrir respostas grandes; provider gerencia toast/text.
        builder.Property(x => x.RespostaJson).HasColumnType("text");

        // Constraint principal: a tripla (Key, EmpresaId, MetodoRecurso) e' unica.
        // Com isso, mesmo cliente reenviando o mesmo Idempotency-Key para um
        // recurso diferente nao faz colisao.
        builder.HasIndex(x => new { x.Key, x.EmpresaId, x.MetodoRecurso })
            .IsUnique()
            .HasDatabaseName("ux_idempotency_key_empresa_recurso");

        // Indice auxiliar para job de cleanup.
        builder.HasIndex(x => x.ExpiraEm)
            .HasDatabaseName("ix_idempotency_expira");
    }
}
