using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// #2003 (estoque-02): o pedido entregue seguia ocupando a vaga da janela. A Agenda mostrava "3 de 3" com três
/// pedidos já entregues e a capacidade do site continuava cheia. A vaga fica (o KDS, a Agenda e a Logística
/// ainda a usam para saber a janela do pedido entregue), mas deixa de contar na capacidade.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class VagaDoPedidoEntregueIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateOnly Segunda = new(2026, 10, 26);

    private sealed record Cenario(Guid EmpresaId, Guid JanelaId, Guid[] PedidosEntregues, Guid PedidoAberto);

    [SkippableFact]
    public async Task PedidosEntregues_LiberamACapacidadeDaJanela_EOAbertoContinuaOcupando()
    {
        var c = await ComecarAsync();

        await using var db = fixture.CreateDbContext();
        var vagas = new VagaOcupadaRepository(db);

        var porPeriodo = await vagas.ContarPorJanelaPeriodoAsync([c.JanelaId], Segunda, Segunda);
        porPeriodo.Should().Equal(new Dictionary<(Guid, DateOnly), int> { [(c.JanelaId, Segunda)] = 1 }, "só o pedido que ainda não foi entregue ocupa");
    }

    [SkippableFact]
    public async Task OcuparAsync_UsaAVagaQueOsEntreguesLiberaram()
    {
        var c = await ComecarAsync();
        var novo = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Pedidos.Add(Pedido(c.EmpresaId, novo, StatusPedidoMapper.Rascunho));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var vaga = await new VagaOcupadaRepository(db).OcuparAsync(c.JanelaId, Segunda, novo);

        vaga.PedidoId.Should().Be(novo, "a janela tem capacidade 4: três entregues e um aberto deixam 3 livres");
    }

    [SkippableFact]
    public async Task VagaDoEntregue_SegueAchavelPeloPedido_ParaKdsAgendaELogistica()
    {
        var c = await ComecarAsync();

        await using var db = fixture.CreateDbContext();
        var porPedido = await new VagaOcupadaRepository(db).GetByPedidoIdsAsync(c.PedidosEntregues);

        porPedido.Keys.Should().BeEquivalentTo(c.PedidosEntregues);
        porPedido.Values.Should().OnlyContain(v => v.Vaga.IsAtiva() && v.Janela.Id == c.JanelaId,
            "liberar a vaga tiraria a janela do pedido entregue das telas que a mostram");
    }

    /// <summary>Janela de capacidade 4 na segunda, cheia: três pedidos que acabaram entregues e um ainda aguardando, cada um com a sua vaga.</summary>
    private async Task<Cenario> ComecarAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var entregues = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var aberto = Guid.NewGuid();
        Guid janelaId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa Vagas", Documento = empresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
            });
            var storefront = Domain.Entities.Storefront.Storefront.Criar(
                empresaId: empresaId, slug: $"sf-{Guid.NewGuid():N}", tituloPublico: "SF Vagas", pedidoMinimoEntrega: 0m);
            storefront.Ativar();
            seed.Storefronts.Add(storefront);
            var janela = JanelaEntrega.Criar(
                storefrontId: storefront.Id, diaDaSemana: (int)Segunda.DayOfWeek,
                horaInicio: new TimeOnly(12, 0), horaFim: new TimeOnly(14, 0), capacidadeMaxima: 4, label: "Almoço");
            seed.JanelasEntrega.Add(janela);
            janelaId = janela.Id;
            foreach (var id in entregues) seed.Pedidos.Add(Pedido(empresaId, id, StatusPedidoMapper.Aguardando));
            seed.Pedidos.Add(Pedido(empresaId, aberto, StatusPedidoMapper.Aguardando));
            await seed.SaveChangesAsync();
        }

        foreach (var pedidoId in entregues.Append(aberto))
        {
            await using var db = fixture.CreateDbContext();
            await new VagaOcupadaRepository(db).OcuparAsync(janelaId, Segunda, pedidoId);
        }

        // A entrega acontece depois de a vaga ser ocupada; hoje nada mexe na vaga (só o cancelamento libera).
        await using (var entrega = fixture.CreateDbContext())
        {
            foreach (var id in entregues)
                (await entrega.Pedidos.IgnoreQueryFilters().SingleAsync(p => p.Id == id)).Status = StatusPedidoMapper.Entregue;
            await entrega.SaveChangesAsync();
        }

        return new Cenario(empresaId, janelaId, entregues, aberto);
    }

    private static Domain.Entities.Pedido Pedido(Guid empresaId, Guid id, string status)
    {
        var p = Domain.Entities.Pedido.Criar(empresaId, origem: "storefront");
        p.Id = id;
        p.Status = status;
        return p;
    }
}
