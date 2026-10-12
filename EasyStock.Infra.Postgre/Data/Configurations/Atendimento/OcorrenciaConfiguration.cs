using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

/// <summary>Ocorrência de pedido (S27).</summary>
public class OcorrenciaConfiguration : IEntityTypeConfiguration<Ocorrencia>
{
    public void Configure(EntityTypeBuilder<Ocorrencia> b)
    {
        b.ToTable("ocorrencias");
        b.HasKey(o => o.Id);

        b.Property(o => o.EmpresaId).IsRequired();
        b.Property(o => o.PedidoId).IsRequired();
        b.Property(o => o.ClienteId).IsRequired();
        b.Property(o => o.Origem).HasConversion<int>().IsRequired();
        b.Property(o => o.Categoria).HasConversion<int>().IsRequired();
        b.Property(o => o.Status).HasConversion<int>().IsRequired();
        b.Property(o => o.Relato).HasMaxLength(Ocorrencia.RelatoTamanhoMaximo).IsRequired();
        b.Property(o => o.Resolucao).HasMaxLength(Ocorrencia.ResolucaoTamanhoMaximo);
        b.Property(o => o.ReembolsoValor).HasPrecision(18, 2);
        b.Property(o => o.ReembolsoIdSolicitacao).HasMaxLength(Ocorrencia.ReembolsoIdTamanhoMaximo);
        b.Property(o => o.CriadaEm).IsRequired();
        b.Property(o => o.ApuradaPorNome).HasMaxLength(120);
        b.Property(o => o.ResolvidaPorNome).HasMaxLength(120);

        // Lista do console: por status, mais recentes primeiro.
        b.HasIndex(o => new { o.EmpresaId, o.Status, o.CriadaEm })
            .HasDatabaseName("ix_ocorrencias_empresa_status_criada");
        b.HasIndex(o => o.PedidoId).HasDatabaseName("ix_ocorrencias_pedido");

        // FK Pedido: RESTRICT, o histórico de reembolso não some com o pedido.
        b.HasOne<Pedido>()
            .WithMany()
            .HasForeignKey(o => o.PedidoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
