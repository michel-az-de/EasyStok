using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public sealed class CreditoClienteComandaConfiguration : IEntityTypeConfiguration<CreditoClienteComanda>
{
    public void Configure(EntityTypeBuilder<CreditoClienteComanda> b)
    {
        b.ToTable("creditos_cliente_comanda");
        b.HasKey(c => c.Id);
        b.Property(c => c.Codigo).HasMaxLength(35).IsRequired();
        b.Property(c => c.Valor).HasPrecision(14, 2);
        b.Property(c => c.Saldo).HasPrecision(14, 2);
        b.HasIndex(c => new { c.EmpresaId, c.OperacaoId }).IsUnique();
        b.HasIndex(c => new { c.EmpresaId, c.Codigo }).IsUnique();
        b.HasOne<Cliente>().WithMany().HasForeignKey(c => c.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Pedido>().WithMany().HasForeignKey(c => c.PedidoOrigemId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Usos).WithOne().HasForeignKey(c => c.CreditoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UsoCreditoClienteComandaConfiguration : IEntityTypeConfiguration<UsoCreditoClienteComanda>
{
    public void Configure(EntityTypeBuilder<UsoCreditoClienteComanda> b)
    {
        b.ToTable("usos_credito_cliente_comanda");
        b.HasKey(c => c.Id);
        b.Property(c => c.Valor).HasPrecision(14, 2);
        b.HasIndex(c => new { c.EmpresaId, c.PedidoId });
        b.HasOne<Pedido>().WithMany().HasForeignKey(c => c.PedidoId).OnDelete(DeleteBehavior.Restrict);
    }
}
