using EasyStock.Domain.Entities.Campanhas;

namespace EasyStock.Infra.Postgre.Data.Configurations.Campanhas;

/// <summary>S28: campanha da dona. Filtro de tenant global + RLS (migration AddCampanhas).</summary>
public class CampanhaConfiguration : IEntityTypeConfiguration<Campanha>
{
    public void Configure(EntityTypeBuilder<Campanha> b)
    {
        b.ToTable("campanhas");
        b.HasKey(x => x.Id);

        b.Ignore(x => x.Filtro);
        b.Ignore(x => x.TagsRestricao);
        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.Nome).IsRequired().HasMaxLength(Campanha.NomeTamanhoMaximo);
        b.Property(x => x.Mensagem).IsRequired().HasMaxLength(Campanha.MensagemTamanhoMaximo);
        b.Property(x => x.ImagemUrl).HasMaxLength(Campanha.ImagemUrlTamanhoMaximo);
        b.Property(x => x.TemplateMeta).HasMaxLength(Campanha.TemplateMetaTamanhoMaximo);
        b.Property(x => x.FiltroJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.TagsRestricaoExcluidas).IsRequired().HasMaxLength(Campanha.TagsRestricaoTamanhoMaximo);
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.CriadaEm).IsRequired();
        b.Property(x => x.CriadaPorUsuarioId).IsRequired();

        // Console: campanhas da empresa por status.
        b.HasIndex(x => new { x.EmpresaId, x.Status });
    }
}

/// <summary>S28: cliente dentro da campanha, único por (CampanhaId, ClienteId).</summary>
public class CampanhaDestinatarioConfiguration : IEntityTypeConfiguration<CampanhaDestinatario>
{
    public void Configure(EntityTypeBuilder<CampanhaDestinatario> b)
    {
        b.ToTable("campanha_destinatarios");
        b.HasKey(x => x.Id);

        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.MotivoExclusao).HasMaxLength(MotivoExclusaoCampanha.TamanhoMaximo);

        b.HasIndex(x => new { x.CampanhaId, x.ClienteId })
            .IsUnique()
            .HasDatabaseName("uq_campanha_destinatarios_campanha_cliente");
        // Limite semanal (S29): campanhas enviadas ao cliente nos últimos 7 dias.
        b.HasIndex(x => new { x.ClienteId, x.EnviadoEm });

        b.ToTable(t => t.HasCheckConstraint(
            "ck_campanha_destinatarios_motivo_exclusao",
            "\"MotivoExclusao\" IS NULL OR \"MotivoExclusao\" IN ("
                + string.Join(',', MotivoExclusaoCampanha.Todos.Select(m => $"'{m}'")) + ")"));

        b.HasOne<Campanha>()
            .WithMany()
            .HasForeignKey(x => x.CampanhaId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(x => x.ClienteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
