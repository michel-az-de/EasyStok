using EasyStock.Domain.Entities.Acervo;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class AcervoImagemConfiguration : IEntityTypeConfiguration<AcervoImagem>
{
    public void Configure(EntityTypeBuilder<AcervoImagem> b)
    {
        b.ToTable("acervo_imagens"); b.HasKey(x => x.Id);
        b.Property(x => x.Titulo).HasMaxLength(160);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.Property(x => x.ItensJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EmpresaId, x.Sha256 }).IsUnique();
        b.HasIndex(x => new { x.EmpresaId, x.CriadoEm });
    }
}

public sealed class AcervoPublicacaoConfiguration : IEntityTypeConfiguration<AcervoPublicacao>
{
    public void Configure(EntityTypeBuilder<AcervoPublicacao> b)
    {
        b.ToTable("acervo_publicacoes"); b.HasKey(x => new { x.EmpresaId, x.Id });
        b.HasIndex(x => new { x.EmpresaId, x.ImagemId, x.CriadoEm });
    }
}

public sealed class AcervoSolicitacaoConfiguration : IEntityTypeConfiguration<AcervoSolicitacao>
{
    public void Configure(EntityTypeBuilder<AcervoSolicitacao> b)
    {
        b.ToTable("acervo_solicitacoes"); b.HasKey(x => x.Id);
        b.Property(x => x.ReferenciasJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EmpresaId, x.ConcluidoEm, x.CriadoEm });
    }
}
