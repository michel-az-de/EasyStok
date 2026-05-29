using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class CategoriaFinanceiraConfiguration : IEntityTypeConfiguration<CategoriaFinanceira>
{
    public void Configure(EntityTypeBuilder<CategoriaFinanceira> b)
    {
        b.ToTable("categorias_financeiras");
        b.HasKey(x => x.Id);

        b.Property(x => x.Nome).IsRequired().HasMaxLength(80);
        b.Property(x => x.Tipo).HasConversion<string>().IsRequired().HasMaxLength(20);
        b.Property(x => x.Cor).HasMaxLength(20);
        b.Property(x => x.Icone).HasMaxLength(60);
        b.Property(x => x.Profundidade).HasDefaultValue(1);
        b.Property(x => x.Ativa).HasDefaultValue(true);
        b.Property(x => x.Ordem).HasDefaultValue(0);

        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmpresaId, x.Ativa }).HasDatabaseName("ix_categorias_financeiras_empresa_ativa");
        b.HasIndex(x => new { x.EmpresaId, x.Tipo }).HasDatabaseName("ix_categorias_financeiras_empresa_tipo");
        b.HasIndex(x => x.ParentId).HasDatabaseName("ix_categorias_financeiras_parent");

        // Unique nome por (empresa, parent) filtrado em ativas — evita conflito com inativas e em ressuscitar
        b.HasIndex(x => new { x.EmpresaId, x.ParentId, x.Nome })
            .IsUnique()
            .HasDatabaseName("ux_categorias_financeiras_empresa_parent_nome")
            .HasFilter("\"Ativa\" = TRUE");
    }
}
