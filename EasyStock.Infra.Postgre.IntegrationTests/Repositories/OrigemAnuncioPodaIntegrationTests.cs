using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>#2928, S6: a poda de 90 dias do id de clique, em Postgres real, atravessando empresas.</summary>
public class OrigemAnuncioPodaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);

    private static OrigemAnuncio Origem(int diasAtras) => OrigemAnuncio.DoSite(
        new OrigemAnuncio.Utms("google", "cpc", "black-friday", null, null),
        new OrigemAnuncio.IdsDeClique("abc123", null, null, null),
        OrigemAnuncio.ViaSite, true, Agora.AddDays(-diasAtras))!;

    [SkippableFact]
    public async Task Poda_anula_o_id_com_mais_de_90_dias_e_mantem_o_resto_em_todas_as_empresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Empresa.Criar("Poda A", "11111111000191");
        var empresaB = Empresa.Criar("Poda B", "22222222000191");

        var pedidoVelho = Pedido.Criar(empresaA.Id);
        pedidoVelho.DefinirOrigemAnuncio(Origem(91));
        var pedidoRecente = Pedido.Criar(empresaA.Id);
        pedidoRecente.DefinirOrigemAnuncio(Origem(89));
        var pedidoSemOrigem = Pedido.Criar(empresaA.Id);
        var pedidoVelhoB = Pedido.Criar(empresaB.Id);
        pedidoVelhoB.DefinirOrigemAnuncio(Origem(200));

        var conversaVelha = Conversa.Abrir(empresaB.Id, "5511999990001", Agora.UtcDateTime.AddDays(-100), "Ana");
        conversaVelha.RegistrarOrigemAnuncio(Origem(91));
        var conversaRecente = Conversa.Abrir(empresaA.Id, "5511999990002", Agora.UtcDateTime.AddDays(-10), "Bia");
        conversaRecente.RegistrarOrigemAnuncio(Origem(89));
        var conversaSemOrigem = Conversa.Abrir(empresaA.Id, "5511999990003", Agora.UtcDateTime.AddDays(-10), "Caio");

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(empresaA, empresaB);
            db.Pedidos.AddRange(pedidoVelho, pedidoRecente, pedidoSemOrigem, pedidoVelhoB);
            db.AtendimentoConversas.AddRange(conversaVelha, conversaRecente, conversaSemOrigem);
            await db.SaveChangesAsync();
        }

        var limite = Agora - OrigemAnuncio.RetencaoIdClique;
        await using (var db = fixture.CreateDbContext())
        {
            var repo = new OrigemAnuncioRepository(db);
            (await repo.PodarIdsDeCliqueAsync(limite)).Should().Be(3, "2 pedidos e 1 conversa vencidos, de duas empresas");
            (await repo.PodarIdsDeCliqueAsync(limite)).Should().Be(0, "idempotente");
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var pedidos = await db.Pedidos.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(p => p.Id);
            var conversas = await db.AtendimentoConversas.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id);

            foreach (var velho in new[] { pedidos[pedidoVelho.Id].OrigemAnuncio!, pedidos[pedidoVelhoB.Id].OrigemAnuncio!, conversas[conversaVelha.Id].OrigemAnuncio! })
            {
                velho.IdClique.Should().BeNull();
                velho.TipoIdClique.Should().BeNull();
                velho.Plataforma.Should().Be(OrigemAnuncio.PlataformaGoogle);
                velho.Campanha.Should().Be("black-friday");
            }
            pedidos[pedidoRecente.Id].OrigemAnuncio!.IdClique.Should().Be("abc123");
            pedidos[pedidoRecente.Id].OrigemAnuncio!.TipoIdClique.Should().Be(OrigemAnuncio.TipoGclid);
            conversas[conversaRecente.Id].OrigemAnuncio!.IdClique.Should().Be("abc123");
            pedidos[pedidoSemOrigem.Id].OrigemAnuncio.Should().BeNull();
            conversas[conversaSemOrigem.Id].OrigemAnuncio.Should().BeNull();
        }
    }
}
