namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>Contador do número curto do lote (#2199), um por empresa e loja.</summary>
public class LoteNumeroContadorConfiguration : IEntityTypeConfiguration<LoteNumeroContador>
{
    public const string Tabela = "lote_numero_contador";

    public void Configure(EntityTypeBuilder<LoteNumeroContador> b)
    {
        b.ToTable(Tabela);
        b.HasKey(c => new { c.EmpresaId, c.LojaId });
        b.Property(c => c.Ultimo).IsRequired();

        b.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
