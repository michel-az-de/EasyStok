using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class ContaBancariaConfiguration : IEntityTypeConfiguration<ContaBancaria>
{
    public void Configure(EntityTypeBuilder<ContaBancaria> b)
    {
        b.ToTable("contas_bancarias");
        b.HasKey(c => c.Id);
        b.Property(c => c.Nome).HasMaxLength(150).IsRequired();
        b.Property(c => c.Tipo).HasMaxLength(20).IsRequired();
        b.Property(c => c.Instituicao).HasMaxLength(150).IsRequired();
        b.Property(c => c.Agencia).HasMaxLength(30).IsRequired();
        b.Property(c => c.Conta).HasMaxLength(50).IsRequired();
        b.Property(c => c.Titular).HasMaxLength(150).IsRequired();
        b.Property(c => c.Revisao).IsConcurrencyToken();
        b.HasOne<Empresa>().WithMany().HasForeignKey(c => c.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(c => new { c.EmpresaId, c.Ativo });
    }
}
