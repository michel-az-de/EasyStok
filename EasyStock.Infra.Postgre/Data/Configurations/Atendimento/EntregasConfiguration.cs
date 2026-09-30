using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class EntregadorConfiguration : IEntityTypeConfiguration<Entregador>
{
    public void Configure(EntityTypeBuilder<Entregador> builder)
    {
        builder.ToTable("entregadores");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Nome).HasMaxLength(Entregador.NomeTamanhoMaximo).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<int>().IsRequired();
        builder.Property(e => e.Empresa).HasConversion<int>().IsRequired();
        builder.Property(e => e.Telefone).HasMaxLength(Entregador.TelefoneTamanhoMaximo);
        builder.Property(e => e.Veiculo).HasMaxLength(Entregador.VeiculoTamanhoMaximo);
        builder.Property(e => e.Placa).HasMaxLength(Entregador.PlacaTamanhoMaximo);

        builder.HasIndex(e => new { e.EmpresaId, e.Ativo, e.Nome }).HasDatabaseName("ix_entregadores_empresa_ativo_nome");
    }
}

public class ViagemConfiguration : IEntityTypeConfiguration<Viagem>
{
    public void Configure(EntityTypeBuilder<Viagem> builder)
    {
        builder.ToTable("viagens");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.EmpresaId).IsRequired();
        builder.Property(v => v.Situacao).HasConversion<int>().IsRequired();
        builder.Property(v => v.CriadaEm).IsRequired();

        builder.HasMany(v => v.Paradas).WithOne().HasForeignKey(p => p.ViagemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Paradas).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_paradas");

        // Entregador apagado não apaga o histórico: a viagem fica sem o vínculo (o retrato das paradas fica).
        builder.HasOne<Entregador>().WithMany().HasForeignKey(v => v.EntregadorId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(v => new { v.EmpresaId, v.Situacao, v.CriadaEm }).HasDatabaseName("ix_viagens_empresa_situacao_criada_em");
    }
}

public class ParadaViagemConfiguration : IEntityTypeConfiguration<ParadaViagem>
{
    public void Configure(EntityTypeBuilder<ParadaViagem> builder)
    {
        builder.ToTable("viagem_paradas");
        builder.HasKey(p => p.Id);

        builder.Ignore(p => p.Entregue);
        builder.Property(p => p.Ordem).IsRequired();
        builder.Property(p => p.EntregadorNome).HasMaxLength(Entregador.NomeTamanhoMaximo);
        builder.Property(p => p.Veiculo).HasMaxLength(Entregador.VeiculoTamanhoMaximo);
        builder.Property(p => p.Placa).HasMaxLength(Entregador.PlacaTamanhoMaximo);
        builder.Property(p => p.EmpresaEntregador).HasConversion<int?>();

        builder.HasOne<Domain.Entities.Pedido>().WithMany().HasForeignKey(p => p.PedidoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.ViagemId, p.PedidoId }).IsUnique().HasDatabaseName("ux_viagem_paradas_viagem_pedido");
        builder.HasIndex(p => p.PedidoId).HasDatabaseName("ix_viagem_paradas_pedido");
    }
}

public class ChamadoEntregadorConfiguration : IEntityTypeConfiguration<ChamadoEntregador>
{
    public void Configure(EntityTypeBuilder<ChamadoEntregador> builder)
    {
        builder.ToTable("chamados_entregador");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.Texto).HasMaxLength(ChamadoEntregador.TextoTamanhoMaximo).IsRequired();
        builder.Property(c => c.Situacao).HasConversion<int>().IsRequired();
        builder.Property(c => c.AbertoEm).IsRequired();

        builder.HasOne<Viagem>().WithMany().HasForeignKey(c => c.ViagemId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(c => new { c.EmpresaId, c.Situacao, c.AbertoEm }).HasDatabaseName("ix_chamados_entregador_empresa_situacao_aberto_em");
    }
}
