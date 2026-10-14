using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Infra.Postgre.Data.Configurations.Operacao;

/// <summary>Fila de impressão do canhoto (S20).</summary>
public class ImpressaoPendenteConfiguration : IEntityTypeConfiguration<ImpressaoPendente>
{
    public void Configure(EntityTypeBuilder<ImpressaoPendente> b)
    {
        b.ToTable("impressoes_pendentes");
        b.HasKey(i => i.Id);

        b.Property(i => i.EmpresaId).IsRequired();
        b.Property(i => i.PedidoId).IsRequired();
        b.Property(i => i.Tipo).HasConversion<int>().IsRequired();
        b.Property(i => i.Status).HasConversion<int>().IsRequired();
        b.Property(i => i.CriadaEm).IsRequired();
        b.Property(i => i.Tentativas).IsRequired();
        b.Property(i => i.Erro).HasMaxLength(ImpressaoPendente.ErroTamanhoMaximo);
        b.Property(i => i.Documento).HasMaxLength(24);
        b.Property(i => i.Modelo).HasMaxLength(24);
        b.Property(i => i.Codigo).HasMaxLength(16);
        b.Property(i => i.Perfil).HasMaxLength(80);
        b.Property(i => i.Versao).HasMaxLength(40);
        b.Property(i => i.Hash).HasMaxLength(64);
        b.Property(i => i.Intencao).HasMaxLength(64);
        b.HasIndex(i => new { i.EmpresaId, i.SolicitacaoId }).IsUnique()
            .HasDatabaseName("ix_impressoes_empresa_solicitacao");

        // Polling do consumidor (pendentes da empresa pela ordem de chegada) e varredura do alerta.
        b.HasIndex(i => new { i.EmpresaId, i.Status, i.CriadaEm })
            .HasDatabaseName("ix_impressoes_pendentes_empresa_status_criada");

        // FK Pedido — CASCADE: a impressão não existe sem o pedido.
        b.HasOne<Pedido>()
            .WithMany()
            .HasForeignKey(i => i.PedidoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
