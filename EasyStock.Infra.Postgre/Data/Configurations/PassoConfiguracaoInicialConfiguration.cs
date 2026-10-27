using EasyStock.Domain.Services;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>Marcas dos passos da configuração inicial (#2070): uma linha por empresa e passo, gravada por upsert.</summary>
public class PassoConfiguracaoInicialConfiguration : IEntityTypeConfiguration<PassoConfiguracaoInicial>
{
    public const string Tabela = "configuracao_inicial_passos";

    public void Configure(EntityTypeBuilder<PassoConfiguracaoInicial> b)
    {
        b.ToTable(Tabela, t => t.HasCheckConstraint(
            "ck_configuracao_inicial_passos_situacao",
            $"\"Situacao\" IN ('{SituacoesDoPasso.TextoConferido}', '{SituacoesDoPasso.TextoDepois}')"));
        b.HasKey(p => new { p.EmpresaId, p.Passo });

        b.Property(p => p.Passo).HasMaxLength(ConfiguracaoInicial.TamanhoMaximoDoId).IsRequired();
        b.Property(p => p.Situacao)
            .HasConversion(s => SituacoesDoPasso.Texto(s), t => SituacoesDoPasso.Ler(t)!.Value)
            .HasMaxLength(20)
            .IsRequired();
        b.Property(p => p.Em).IsRequired();
        b.Property(p => p.PorUsuarioId).IsRequired();

        b.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(p => p.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
