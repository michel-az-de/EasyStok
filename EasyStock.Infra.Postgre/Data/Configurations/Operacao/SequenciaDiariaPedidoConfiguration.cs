using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Infra.Postgre.Data.Configurations.Operacao;

/// <summary>Contador do número do dia do pedido (S53), um por empresa e dia de produção.</summary>
public class SequenciaDiariaPedidoConfiguration : IEntityTypeConfiguration<SequenciaDiariaPedido>
{
    public const string Tabela = "sequencias_pedido_dia";

    public void Configure(EntityTypeBuilder<SequenciaDiariaPedido> b)
    {
        b.ToTable(Tabela);
        b.HasKey(s => new { s.EmpresaId, s.Data });
        b.Property(s => s.Ultimo).IsRequired();

        b.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(s => s.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
