using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>Ocorrência (S27) em Postgres real: listagem por status e isolamento de tenant.</summary>
public class OcorrenciaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private static async Task<Pedido> CriarPedidoAsync(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private static Ocorrencia Nova(Guid empresaId, Guid pedidoId, DateTime em) =>
        Ocorrencia.Abrir(empresaId, pedidoId, Guid.NewGuid(), null, OrigemOcorrencia.Dona,
            CategoriaOcorrencia.Atraso, "demorou", em);

    [SkippableFact]
    public async Task ListaPorStatusMaisRecentesPrimeiroEPersisteReembolso()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var pedido = await CriarPedidoAsync(db, empresa);
        var repo = new OcorrenciaRepository(db);
        var antiga = Nova(empresa, pedido.Id, Agora);
        var nova = Nova(empresa, pedido.Id, Agora.AddMinutes(5));
        var resolvida = Nova(empresa, pedido.Id, Agora.AddMinutes(1));
        resolvida.RegistrarReembolso(12.5m, "ref-1", Agora.AddMinutes(2));
        resolvida.Resolver("devolvido", Guid.NewGuid(), Agora.AddMinutes(2));
        await repo.AddAsync(antiga);
        await repo.AddAsync(nova);
        await repo.AddAsync(resolvida);
        await db.SaveChangesAsync();

        var abertas = await repo.ListarAsync(empresa, StatusOcorrencia.Aberta, 10);
        var todas = await repo.ListarAsync(empresa, null, 10);

        abertas.Select(o => o.Id).Should().Equal(nova.Id, antiga.Id);
        todas.Should().HaveCount(3);
        var lida = await repo.ObterAsync(empresa, resolvida.Id);
        lida!.ReembolsoValor.Should().Be(12.5m);
        lida.ReembolsoIdSolicitacao.Should().Be("ref-1");
    }

    [SkippableFact]
    public async Task OutraEmpresaNaoEnxerga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        Guid idB;
        await using (var dbB = fixture.CreateDbContext())
        {
            dbB.SetMobileTenantContext(empresaB);
            var pedido = await CriarPedidoAsync(dbB, empresaB);
            var o = Nova(empresaB, pedido.Id, Agora);
            dbB.Ocorrencias.Add(o);
            await dbB.SaveChangesAsync();
            idB = o.Id;
        }

        await using var dbA = fixture.CreateDbContext();
        dbA.SetMobileTenantContext(empresaA);
        var repo = new OcorrenciaRepository(dbA);

        (await repo.ObterAsync(empresaA, idB)).Should().BeNull();
        (await repo.ListarAsync(empresaA, null, 10)).Should().BeEmpty();
    }
}
