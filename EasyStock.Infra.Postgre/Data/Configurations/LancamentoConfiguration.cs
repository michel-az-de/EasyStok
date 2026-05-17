using EasyStock.Domain.Financeiro;
using EasyStock.Domain.Financeiro.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class LancamentoConfiguration : IEntityTypeConfiguration<Lancamento>
{
    public void Configure(EntityTypeBuilder<Lancamento> b)
    {
        b.ToTable("lancamentos");
        b.HasKey(l => l.Id);

        // Optimistic concurrency via xmin (system column, sem migration extra).
        b.Property(l => l.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.Property(l => l.EmpresaId).IsRequired();
        b.Property(l => l.Tipo).HasConversion<int>().IsRequired();
        b.Property(l => l.Status).HasConversion<int>().IsRequired();
        b.Property(l => l.Descricao).IsRequired().HasMaxLength(200);
        b.Property(l => l.Valor).HasColumnType("numeric(14,2)").IsRequired();
        b.Property(l => l.DataEmissao).IsRequired();
        b.Property(l => l.DataVencimento).IsRequired();
        b.Property(l => l.Categoria).HasMaxLength(60);
        b.Property(l => l.DocumentoReferencia).HasMaxLength(120);
        b.Property(l => l.Observacoes).HasColumnType("text");
        b.Property(l => l.MotivoCancelamento).HasMaxLength(500);
        b.Property(l => l.CriadoEm).IsRequired();
        b.Property(l => l.AlteradoEm).IsRequired();

        b.HasMany(l => l.Baixas)
            .WithOne()
            .HasForeignKey(x => x.LancamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indices uteis para consultas de listagem e dashboards.
        b.HasIndex(l => new { l.EmpresaId, l.Status });
        b.HasIndex(l => new { l.EmpresaId, l.DataVencimento });
        b.HasIndex(l => new { l.EmpresaId, l.Tipo });

        // Eventos pendentes nao sao persistidos — vivem so no agregado em memoria
        // ate o UseCase publicar. O Ignore<LancamentoBaixadoEvent>() do DbContext
        // tira o tipo da convencao para evitar que EF Core o registre como entidade.
        b.Ignore(l => l.EventosPendentes);
        b.Ignore(l => l.TotalBaixado);
        b.Ignore(l => l.ValorRestante);
    }
}

public sealed class LancamentoBaixaConfiguration : IEntityTypeConfiguration<LancamentoBaixa>
{
    public void Configure(EntityTypeBuilder<LancamentoBaixa> b)
    {
        b.ToTable("lancamento_baixas");
        b.HasKey(x => x.Id);
        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.LancamentoId).IsRequired();
        b.Property(x => x.Valor).HasColumnType("numeric(14,2)").IsRequired();
        b.Property(x => x.DataBaixa).IsRequired();
        b.Property(x => x.MeioPagamento).IsRequired().HasMaxLength(20);
        b.Property(x => x.ChaveExterna).HasMaxLength(120);
        b.Property(x => x.Observacao).HasColumnType("text");
        b.Property(x => x.RegistradoPorNome).HasMaxLength(120);
        b.Property(x => x.MotivoEstorno).HasMaxLength(500);
        b.Property(x => x.CriadoEm).IsRequired();

        b.HasIndex(x => new { x.EmpresaId, x.DataBaixa });
        // Idempotencia: baixa identificada externamente nao pode duplicar dentro
        // do mesmo lancamento. Filtra fora as baixas sem chave para nao colidirem
        // com os null das demais.
        b.HasIndex(x => new { x.LancamentoId, x.ChaveExterna })
            .IsUnique()
            .HasFilter("\"ChaveExterna\" IS NOT NULL");

        b.Ignore(x => x.Ativa);
    }
}
