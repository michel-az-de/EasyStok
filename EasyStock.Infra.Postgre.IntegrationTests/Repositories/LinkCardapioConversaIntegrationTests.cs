using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Link do cardápio da conversa (S48) em Postgres real: a requisição anônima acha o link pelo hash sem
/// tenant, o uso é atômico (um pedido só), o uso volta quando o pedido é recusado e a tabela tem RLS.
/// </summary>
public class LinkCardapioConversaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task HashSemTenantUsoAtomicoERls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        var hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var hashVencido = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var conversa = Conversa.Abrir(empresa, "5511999998888", agora);
        var link = LinkCardapioConversa.Gerar(empresa, conversa.Id, hash, agora);
        var vencido = LinkCardapioConversa.Gerar(empresa, conversa.Id, hashVencido, agora.AddDays(-2));
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.AtendimentoConversas.Add(conversa);
            db.LinksCardapioConversa.AddRange(link, vencido);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            // Requisição anônima: nenhum tenant ligado.
            var achado = await new LinkCardapioConversaRepository(db).ObterPorTokenHashAsync(hash);
            achado!.Id.Should().Be(link.Id);
            achado.EmpresaId.Should().Be(empresa);
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var repo = new LinkCardapioConversaRepository(db);
            (await repo.TentarConsumirAsync(empresa, link.Id, agora)).Should().BeTrue();
            (await repo.TentarConsumirAsync(empresa, link.Id, agora)).Should().BeFalse("o link serve para um pedido só");
            await repo.LiberarAsync(empresa, link.Id);
            (await repo.TentarConsumirAsync(empresa, link.Id, agora)).Should().BeTrue("pedido recusado devolve o uso");
            (await repo.TentarConsumirAsync(empresa, vencido.Id, agora)).Should().BeFalse("venceu");
            (await repo.TentarConsumirAsync(Guid.NewGuid(), link.Id, agora)).Should().BeFalse("outra empresa");
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            (await db.Database
                .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'links_cardapio_conversa'")
                .SingleAsync())
                .Should().Be(1);
        }
    }
}
