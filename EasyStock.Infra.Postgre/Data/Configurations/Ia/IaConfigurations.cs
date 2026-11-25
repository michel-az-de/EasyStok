using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Integration;

namespace EasyStock.Infra.Postgre.Data.Configurations.Ia;

// #2957 (S1 de #2948): provedores, modelos e chamadas de IA por loja. Colunas em snake_case como
// credencial_integracao; RLS na migration. FKs compostas (empresa_id, id) impedem referência entre lojas.

public class ProvedorIaConfiguration : IEntityTypeConfiguration<ProvedorIa>
{
    public void Configure(EntityTypeBuilder<ProvedorIa> b)
    {
        // #3304 (R8): os limites do CHECK saem das constantes do domínio, então os dois nunca divergem.
        b.ToTable("provedor_ia", t => t.HasCheckConstraint("ck_provedor_ia_prazo_segundos",
            $"prazo_segundos IS NULL OR (prazo_segundos >= {ProvedorIa.PrazoMinimoSegundos} AND prazo_segundos <= {ProvedorIa.PrazoMaximoSegundos})"));
        b.HasKey(p => p.Id);
        b.HasAlternateKey(p => new { p.EmpresaId, p.Id }).HasName("ak_provedor_ia_empresa_id");
        b.Ignore(p => p.ProviderKey);

        // PK gerada no app (ADR-0028).
        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(p => p.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(p => p.Nome).HasColumnName("nome").HasMaxLength(ProvedorIa.NomeTamanhoMaximo).IsRequired();
        b.Property(p => p.BaseUrl).HasColumnName("base_url").HasMaxLength(ProvedorIa.BaseUrlTamanhoMaximo).IsRequired();
        b.Property(p => p.Autenticacao).HasColumnName("autenticacao").HasConversion<int>().IsRequired();
        b.Property(p => p.Ligado).HasColumnName("ligado").IsRequired();
        b.Property(p => p.OrcamentoMensalUsd).HasColumnName("orcamento_mensal_usd").HasPrecision(12, 6);
        b.Property(p => p.PrazoSegundos).HasColumnName("prazo_segundos");
        b.Property(p => p.CredencialId).HasColumnName("credencial_id");
        b.Property(p => p.CriadoEm).HasColumnName("criado_em").IsRequired();
        b.Property(p => p.AlteradoEm).HasColumnName("alterado_em").IsRequired();

        b.HasOne<CredencialIntegracao>().WithMany().HasForeignKey(p => p.CredencialId)
            .HasConstraintName("fk_provedor_ia_credencial").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.CredencialId).IsUnique().HasDatabaseName("ux_provedor_ia_credencial");
    }
}

public class ModeloIaConfiguration : IEntityTypeConfiguration<ModeloIa>
{
    public void Configure(EntityTypeBuilder<ModeloIa> b)
    {
        b.ToTable("modelo_ia", t =>
        {
            t.HasCheckConstraint("ck_modelo_ia_padrao_ligado", "NOT padrao OR ligado");
            t.HasCheckConstraint("ck_modelo_ia_fator_fora_pico", "fator_fora_pico > 0 AND fator_fora_pico <= 1");
            t.HasCheckConstraint("ck_modelo_ia_saida_max",
                $"saida_max IS NULL OR (saida_max >= {ModeloIa.SaidaMaxMinima} AND saida_max <= {ModeloIa.SaidaMaxMaxima})");
        });
        b.HasKey(m => m.Id);
        b.HasAlternateKey(m => new { m.EmpresaId, m.Id }).HasName("ak_modelo_ia_empresa_id");

        b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(m => m.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(m => m.ProvedorId).HasColumnName("provedor_id").IsRequired();
        b.Property(m => m.IdExterno).HasColumnName("id_externo").HasMaxLength(ModeloIa.IdExternoTamanhoMaximo).IsRequired();
        b.Property(m => m.Rotulo).HasColumnName("rotulo").HasMaxLength(ModeloIa.RotuloTamanhoMaximo).IsRequired();
        b.Property(m => m.Ligado).HasColumnName("ligado").IsRequired();
        b.Property(m => m.Padrao).HasColumnName("padrao").IsRequired();
        b.Property(m => m.PrecoEntrada).HasColumnName("preco_entrada").HasPrecision(12, 6);
        b.Property(m => m.PrecoSaida).HasColumnName("preco_saida").HasPrecision(12, 6);
        b.Property(m => m.PrecoCacheLido).HasColumnName("preco_cache_lido").HasPrecision(12, 6);
        b.Property(m => m.FatorForaPico).HasColumnName("fator_fora_pico").HasPrecision(4, 3).HasDefaultValue(1m).IsRequired();
        b.Property(m => m.SaidaMax).HasColumnName("saida_max");
        b.Property(m => m.CriadoEm).HasColumnName("criado_em").IsRequired();
        b.Property(m => m.AlteradoEm).HasColumnName("alterado_em").IsRequired();

        b.HasOne<ProvedorIa>().WithMany()
            .HasForeignKey(m => new { m.EmpresaId, m.ProvedorId })
            .HasPrincipalKey(p => new { p.EmpresaId, p.Id })
            .HasConstraintName("fk_modelo_ia_provedor").OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(m => new { m.EmpresaId, m.ProvedorId, m.IdExterno }).IsUnique()
            .HasDatabaseName("ux_modelo_ia_provedor_id_externo");
        // Um só modelo padrão por loja.
        b.HasIndex(m => m.EmpresaId).IsUnique().HasFilter("padrao")
            .HasDatabaseName("ux_modelo_ia_padrao_por_empresa");
    }
}

public class ChamadaIaConfiguration : IEntityTypeConfiguration<ChamadaIa>
{
    public void Configure(EntityTypeBuilder<ChamadaIa> b)
    {
        b.ToTable("chamada_ia");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(c => c.ProvedorId).HasColumnName("provedor_id");
        b.Property(c => c.ModeloId).HasColumnName("modelo_id");
        b.Property(c => c.ModeloRespondido).HasColumnName("modelo_respondido").HasMaxLength(ChamadaIa.ModeloTamanhoMaximo).IsRequired();
        b.Property(c => c.Finalidade).HasColumnName("finalidade").HasConversion<int>().IsRequired();
        b.Property(c => c.TurnoAgenteId).HasColumnName("turno_agente_id");
        b.Property(c => c.TokensEntrada).HasColumnName("tokens_entrada").IsRequired();
        b.Property(c => c.TokensSaida).HasColumnName("tokens_saida").IsRequired();
        b.Property(c => c.TokensCacheLido).HasColumnName("tokens_cache_lido").IsRequired();
        b.Property(c => c.TokensCacheEscrito).HasColumnName("tokens_cache_escrito").IsRequired();
        b.Property(c => c.CustoUsd).HasColumnName("custo_usd").HasPrecision(12, 6);
        b.Property(c => c.DuracaoMs).HasColumnName("duracao_ms").IsRequired();
        b.Property(c => c.Desfecho).HasColumnName("desfecho").HasConversion<int>().IsRequired();
        b.Property(c => c.CriadoEm).HasColumnName("criado_em").IsRequired();

        // Registro só de inserção: provedor e modelo com chamadas não somem, desligam.
        b.HasOne<ProvedorIa>().WithMany()
            .HasForeignKey(c => new { c.EmpresaId, c.ProvedorId })
            .HasPrincipalKey(p => new { p.EmpresaId, p.Id })
            .HasConstraintName("fk_chamada_ia_provedor").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ModeloIa>().WithMany()
            .HasForeignKey(c => new { c.EmpresaId, c.ModeloId })
            .HasPrincipalKey(m => new { m.EmpresaId, m.Id })
            .HasConstraintName("fk_chamada_ia_modelo").OnDelete(DeleteBehavior.Restrict);

        // Soma do mês por provedor sem ler a linha.
        b.HasIndex(c => new { c.EmpresaId, c.ProvedorId, c.CriadoEm })
            .IncludeProperties(c => c.CustoUsd)
            .HasDatabaseName("ix_chamada_ia_empresa_provedor_criado");
    }
}
