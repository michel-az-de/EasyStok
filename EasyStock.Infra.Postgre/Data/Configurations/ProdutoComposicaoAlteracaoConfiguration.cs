namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ProdutoComposicaoAlteracaoConfiguration : IEntityTypeConfiguration<ProdutoComposicaoAlteracao>
{
    public void Configure(EntityTypeBuilder<ProdutoComposicaoAlteracao> builder)
    {
        builder.ToTable("produtos_composicao_alteracao");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).ValueGeneratedOnAdd().HasColumnType("uuid");
        builder.Property(a => a.EmpresaId).HasColumnType("uuid").IsRequired();
        builder.Property(a => a.ProdutoFinalId).HasColumnType("uuid").IsRequired();
        builder.Property(a => a.LojaId).HasColumnType("uuid");
        builder.Property(a => a.UsuarioId).HasColumnType("uuid").IsRequired();

        builder.Property(a => a.Acao)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnType("character varying(50)");

        builder.Property(a => a.AlteracoesJson).HasColumnType("text");

        builder.Property(a => a.Observacao)
            .HasMaxLength(500)
            .HasColumnType("character varying(500)");

        builder.Property(a => a.AlteradoEm)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(a => a.ProdutoFinal)
            .WithMany()
            .HasForeignKey(a => a.ProdutoFinalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Loja)
            .WithMany()
            .HasForeignKey(a => a.LojaId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(a => a.Usuario)
            .WithMany()
            .HasForeignKey(a => a.UsuarioId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(a => new { a.ProdutoFinalId, a.AlteradoEm });
        builder.HasIndex(a => new { a.EmpresaId, a.AlteradoEm })
            .HasDatabaseName("ix_produtos_composicao_alteracao_empresa_alterado_em");
    }
}
