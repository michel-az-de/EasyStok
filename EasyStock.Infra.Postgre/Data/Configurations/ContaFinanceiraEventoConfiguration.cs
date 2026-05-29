using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ContaFinanceiraEventoConfiguration : IEntityTypeConfiguration<ContaFinanceiraEvento>
{
    public void Configure(EntityTypeBuilder<ContaFinanceiraEvento> b)
    {
        b.ToTable("contas_financeiras_eventos", t =>
        {
            t.HasCheckConstraint(
                "ck_contas_financeiras_eventos_lado_xor",
                "(\"ContaPagarId\" IS NOT NULL AND \"ContaReceberId\" IS NULL) " +
                "OR (\"ContaPagarId\" IS NULL AND \"ContaReceberId\" IS NOT NULL)");
        });
        b.HasKey(x => x.Id);

        b.Property(x => x.TipoEvento).HasConversion<string>().IsRequired().HasMaxLength(40);
        b.Property(x => x.Descricao).HasMaxLength(500);
        b.Property(x => x.ValorAntes).HasMaxLength(120);
        b.Property(x => x.ValorDepois).HasMaxLength(120);
        b.Property(x => x.UsuarioNome).HasMaxLength(120);
        b.Property(x => x.Origem).HasMaxLength(20);

        b.HasOne(x => x.ContaPagar).WithMany().HasForeignKey(x => x.ContaPagarId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ContaReceber).WithMany().HasForeignKey(x => x.ContaReceberId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ContaPagarId, x.CriadoEm }).HasDatabaseName("ix_contas_fin_eventos_cp_data");
        b.HasIndex(x => new { x.ContaReceberId, x.CriadoEm }).HasDatabaseName("ix_contas_fin_eventos_cr_data");
        b.HasIndex(x => new { x.EmpresaId, x.TipoEvento, x.CriadoEm }).HasDatabaseName("ix_contas_fin_eventos_empresa_tipo_data");
    }
}
