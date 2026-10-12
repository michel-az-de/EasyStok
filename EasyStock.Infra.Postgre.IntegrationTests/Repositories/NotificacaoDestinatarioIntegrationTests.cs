using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class NotificacaoDestinatarioIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ListasContagensEResumo_IsolamDestinatariosEMarcarTodasPreservaAvisosAlheios()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var casa = Empresa.Criar("Casa avisos teste", null);
        var outra = Empresa.Criar("Outra empresa teste", null);
        db.Empresas.AddRange(casa, outra);
        var pessoa = Guid.NewGuid();
        var colega = Guid.NewGuid();
        var avisos = new[] { (casa.Id, (Guid?)null), (casa.Id, (Guid?)pessoa), (casa.Id, (Guid?)colega), (outra.Id, (Guid?)pessoa) }
            .Select(a => { var n = Notificacao.Criar(a.Id, TipoAlertaEstoque.PedidoRecebido, "Aviso de teste"); n.UsuarioId = a.Item2; return n; }).ToArray();
        db.Notificacoes.AddRange(avisos);
        await db.SaveChangesAsync();
        db.SetMobileTenantContext(casa.Id);
        var repo = new NotificacaoRepository(db);
        var esperados = avisos.Take(2).Select(a => a.Id);
        (await repo.GetByEmpresaAsync(casa.Id, usuarioId: pessoa)).Items.Select(a => a.Id).Should().BeEquivalentTo(esperados);
        (await repo.GetRecentesNaoLidasAsync(casa.Id, 10, pessoa)).Select(a => a.Id).Should().BeEquivalentTo(esperados);
        (await repo.CountNaoLidasAsync(casa.Id, pessoa)).Should().Be(2);
        (await repo.GetResumoAsync(casa.Id, pessoa)).TotalNaoLidas.Should().Be(2);
        await repo.MarcarTodasComoLidasAsync(casa.Id, pessoa);
        (await repo.CountNaoLidasAsync(casa.Id, pessoa)).Should().Be(0);
        (await repo.CountNaoLidasAsync(casa.Id, colega)).Should().Be(1);
        db.ChangeTracker.Clear();
        var todos = await db.Notificacoes.IgnoreQueryFilters().ToListAsync();
        todos.Where(n => n.Lida).Select(n => n.Id).Should().BeEquivalentTo(esperados);
        todos.Single(n => n.Id == avisos[3].Id).Lida.Should().BeFalse();
    }
}
