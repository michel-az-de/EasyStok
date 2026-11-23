using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class ModeloMensagemLocalConfiguration : IEntityTypeConfiguration<ModeloMensagemLocal>
{
    public void Configure(EntityTypeBuilder<ModeloMensagemLocal> builder)
    {
        builder.ToTable("modelos_mensagem");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.EmpresaId).IsRequired();
        builder.Property(m => m.Nome).HasMaxLength(ModeloMensagemLocal.NomeTamanhoMaximo).IsRequired();
        builder.Property(m => m.Idioma).HasMaxLength(ModeloMensagemLocal.IdiomaTamanhoMaximo).IsRequired();
        builder.Property(m => m.Categoria).HasMaxLength(ModeloMensagemLocal.CategoriaTamanhoMaximo).IsRequired();
        builder.Property(m => m.Cabecalho).HasMaxLength(ModeloMensagemLocal.CabecalhoTamanhoMaximo);
        builder.Property(m => m.Corpo).HasMaxLength(ModeloMensagemLocal.CorpoTamanhoMaximo).IsRequired();
        builder.Property(m => m.Rodape).HasMaxLength(ModeloMensagemLocal.RodapeTamanhoMaximo);
        builder.Property(m => m.CriadoEm).IsRequired();
        builder.Property(m => m.AlteradoEm).IsRequired();
        builder.Ignore(m => m.QuantidadeParametros);

        // #3204: nome + idioma único por empresa, contando os arquivados (a Application confere antes e devolve 409;
        // o índice fecha a corrida).
        builder.HasIndex(m => new { m.EmpresaId, m.Nome, m.Idioma })
            .IsUnique()
            .HasDatabaseName("ux_modelos_mensagem_empresa_nome_idioma");
    }
}
