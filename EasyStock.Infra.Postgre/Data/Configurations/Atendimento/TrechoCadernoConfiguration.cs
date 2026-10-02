using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

/// <summary>S54: trechos do caderno da loja, por empresa, com RLS (migration).</summary>
public class TrechoCadernoConfiguration : IEntityTypeConfiguration<TrechoCaderno>
{
    public void Configure(EntityTypeBuilder<TrechoCaderno> builder)
    {
        builder.ToTable("caderno_trechos");
        builder.HasKey(t => t.Id);
        // PK gerada no app (ADR-0028): nunca inferir UPDATE de linha inexistente.
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Ignore(t => t.Codigo);

        builder.Property(t => t.EmpresaId).IsRequired();
        builder.Property(t => t.Titulo).HasMaxLength(TrechoCaderno.TituloTamanhoMaximo).IsRequired();
        builder.Property(t => t.Texto).HasMaxLength(TrechoCaderno.TextoTamanhoMaximo).IsRequired();
        builder.Property(t => t.PalavrasChave).HasMaxLength(TrechoCaderno.PalavrasChaveTamanhoMaximo).IsRequired();
        builder.Property(t => t.CriadoEm).IsRequired();
        builder.Property(t => t.AlteradoEm).IsRequired();

        // O agente lê os ativos da empresa a cada turno.
        builder.HasIndex(t => new { t.EmpresaId, t.Arquivado })
            .HasDatabaseName("ix_caderno_trechos_empresa_arquivado");
    }
}
