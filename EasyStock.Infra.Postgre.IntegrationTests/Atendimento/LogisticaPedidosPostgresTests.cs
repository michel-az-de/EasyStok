using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>
/// #2018: a agenda de Entregas (<see cref="LogisticaRepository.PedidosAsync"/>) passou a projetar as linhas avulsas do pedido
/// numa coleção aninhada e a escolher o frete pela regra única (<see cref="PedidoItem.EhFrete"/>). Os testes de logística usam
/// mock; só o Postgres prova que o EF traduz a consulta e que o frete não soma prato nem taxa da casa.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class LogisticaPedidosPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateOnly Segunda = new(2026, 10, 26);

    [SkippableFact]
    public async Task PedidosAsync_TraduzNoBanco_EOFreteESoALinhaDeEntrega()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var comFrete = Guid.NewGuid();
        var semFrete = Guid.NewGuid();
        Guid janelaId;

        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            seed.Empresas.Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa Logística", Documento = empresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
            });
            var storefront = Domain.Entities.Storefront.Storefront.Criar(
                empresaId: empresaId, slug: $"sf-{Guid.NewGuid():N}", tituloPublico: "SF Logística", pedidoMinimoEntrega: 0m);
            storefront.Ativar();
            seed.Storefronts.Add(storefront);
            var janela = JanelaEntrega.Criar(
                storefrontId: storefront.Id, diaDaSemana: (int)Segunda.DayOfWeek,
                horaInicio: new TimeOnly(12, 0), horaFim: new TimeOnly(14, 0), capacidadeMaxima: 4, label: "Almoço");
            seed.JanelasEntrega.Add(janela);
            janelaId = janela.Id;

            var pedido = Pedido(empresaId, comFrete, "Ana");
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = comFrete, Nome = "Marmita", LinhaSnapshot = "{}", Quantidade = 1, PrecoUnitario = 40m, Subtotal = 40m });
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = comFrete, Nome = "Entrega — Centro", Quantidade = 1, PrecoUnitario = 12.5m, Subtotal = 12.5m });
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = comFrete, Nome = "Taxa de embalagem", Quantidade = 1, PrecoUnitario = 2m, Subtotal = 2m });
            seed.Pedidos.Add(pedido);

            var retirada = Pedido(empresaId, semFrete, "Bia");
            retirada.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = semFrete, Nome = "Marmita", LinhaSnapshot = "{}", Quantidade = 2, PrecoUnitario = 40m, Subtotal = 80m });
            seed.Pedidos.Add(retirada);
            await seed.SaveChangesAsync();
        }

        foreach (var pedidoId in new[] { comFrete, semFrete })
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresaId);
            await new VagaOcupadaRepository(db).OcuparAsync(janelaId, Segunda, pedidoId);
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        // PedidosAsync não usa os dois use cases da viagem nem do caixa.
        var repositorio = new LogisticaRepository(leitura, null!, null!);

        var pedidos = await repositorio.PedidosAsync(empresaId, Segunda, null, CancellationToken.None);

        pedidos.Should().HaveCount(2);
        pedidos.Single(p => p.Id == comFrete).FreteCentavos.Should().Be(1250, "só a linha \"Entrega — \" é frete; prato e taxa da casa ficam de fora");
        pedidos.Single(p => p.Id == semFrete).FreteCentavos.Should().Be(0);
    }

    private static Domain.Entities.Pedido Pedido(Guid empresaId, Guid id, string cliente)
    {
        var p = Domain.Entities.Pedido.Criar(empresaId, origem: "storefront");
        p.Id = id;
        p.Status = StatusPedidoMapper.Pronto;
        p.ClienteNome = cliente;
        return p;
    }
}
