using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// F10-B — Configuracao da tabela <c>entity_alteracoes</c>.
/// Indices otimizados pra lookup por entidade e retention cleanup.
/// </summary>
public class EntityAlteracaoConfiguration : IEntityTypeConfiguration<EntityAlteracao>
{
    public void Configure(EntityTypeBuilder<EntityAlteracao> builder)
    {
        builder.ToTable("entity_alteracoes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd()
            .HasColumnType("uuid");

        builder.Property(e => e.EmpresaId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(e => e.TipoEntidade)
            .IsRequired()
            .HasMaxLength(60)
            .HasColumnType("character varying(60)");

        builder.Property(e => e.EntidadeId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(e => e.Acao)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("character varying(20)");

        builder.Property(e => e.Campo)
            .HasMaxLength(60)
            .HasColumnType("character varying(60)");

        builder.Property(e => e.ValorAntigo)
            .HasColumnType("text");

        builder.Property(e => e.ValorNovo)
            .HasColumnType("text");

        builder.Property(e => e.AlteradoPorUserId)
            .HasColumnType("uuid");

        builder.Property(e => e.AlteradoPorNome)
            .HasMaxLength(120)
            .HasColumnType("character varying(120)");

        builder.Property(e => e.Origem)
            .HasMaxLength(20)
            .HasColumnType("character varying(20)");

        builder.Property(e => e.AlteradoEm)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.AlteracoesJson)
            .HasColumnType("text");

        builder.Property(e => e.PiiCriptografado)
            .HasColumnType("text");

        // Lookup por entidade especifica (ex: historico de 1 Pedido)
        builder.HasIndex(e => new { e.EmpresaId, e.TipoEntidade, e.EntidadeId, e.AlteradoEm })
            .HasDatabaseName("ix_entity_alteracoes_lookup")
            .IsDescending(false, false, false, true);

        // Retention cleanup (DELETE WHERE AlteradoEm < threshold por tenant)
        builder.HasIndex(e => new { e.EmpresaId, e.AlteradoEm })
            .HasDatabaseName("ix_entity_alteracoes_retention");
    }
}
