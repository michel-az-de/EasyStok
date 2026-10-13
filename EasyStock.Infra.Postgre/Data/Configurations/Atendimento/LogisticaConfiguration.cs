using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public sealed class CotacaoLogisticaConfiguration : IEntityTypeConfiguration<CotacaoLogistica>
{
    public void Configure(EntityTypeBuilder<CotacaoLogistica> b)
    {
        b.ToTable("logistica_cotacoes"); b.HasKey(x => x.Id);
        b.Property(x => x.Provedor).HasMaxLength(40);
        b.Property(x => x.Referencia).HasMaxLength(300);
        b.Property(x => x.SolicitacaoJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EmpresaId, x.CriadoEm });
        b.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DespachoLogisticaConfiguration : IEntityTypeConfiguration<DespachoLogistica>
{
    public void Configure(EntityTypeBuilder<DespachoLogistica> b)
    {
        b.ToTable("logistica_despachos"); b.HasKey(x => x.Id);
        b.Property(x => x.Provedor).HasMaxLength(40);
        b.Property(x => x.Status).HasMaxLength(30);
        b.Property(x => x.ChaveIdempotencia).HasMaxLength(100);
        b.Property(x => x.ChavePagamento).HasMaxLength(100);
        b.Property(x => x.PedidoIdsJson).HasColumnType("jsonb");
        b.Property(x => x.EventosJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EmpresaId, x.ChaveIdempotencia }).IsUnique();
        b.HasIndex(x => new { x.EmpresaId, x.CotacaoId }).IsUnique();
        b.HasIndex(x => x.MovimentoCaixaId).IsUnique();
        b.HasOne<CotacaoLogistica>().WithMany().HasForeignKey(x => x.CotacaoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Viagem>().WithMany().HasForeignKey(x => x.ViagemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MovimentoCaixa>().WithMany().HasForeignKey(x => x.MovimentoCaixaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PoliticaLogisticaConfiguration : IEntityTypeConfiguration<PoliticaLogistica>
{
    public void Configure(EntityTypeBuilder<PoliticaLogistica> b)
    {
        b.ToTable("logistica_politicas"); b.HasKey(x => x.EmpresaId);
        b.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
    }
}
