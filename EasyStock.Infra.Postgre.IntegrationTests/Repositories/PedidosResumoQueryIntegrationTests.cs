using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #3294: os indicadores da tela Pedidos rodam no Postgres sobre os mesmos filtros da lista. Vendido, a receber,
/// atrasados, primeira compra e ordem da compra nunca somam outra empresa, nem quando o pedido alheio aponta o
/// mesmo cliente; e o resumo bate com a soma das linhas da lista paginada.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PedidosResumoQueryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task ResumoSinaisEListaBatemSemMisturarEmpresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Resumo #3294", Documento());
        var outra = Empresa.Criar("Resumo alheio #3294", Documento());
        var ana = Cliente.Criar(empresa.Id, "Ana Resumo 3294");
        var bia = Cliente.Criar(empresa.Id, "Bia Resumo 3294");
        var zeca = Cliente.Criar(outra.Id, "Zeca Resumo 3294");

        var p1 = NovoPedido(empresa.Id, ana, StatusPedidoMapper.Entregue, 100m, Agora.AddDays(-10), pago: 100m, itens: [("Bolo", 2m)]);
        var p2 = NovoPedido(empresa.Id, ana, StatusPedidoMapper.Preparando, 50m, Agora.AddDays(-2), pago: 20m, agendado: Agora.AddHours(-1), itens: [("Bolo", 1m), ("Torta", 1m)]);
        p2.Eventos.Add(Evento(p2, StatusPedidoMapper.Aguardando, Agora.AddDays(-1)));
        p2.Eventos.Add(Evento(p2, StatusPedidoMapper.Preparando, Agora.AddHours(-3)));
        var p3 = NovoPedido(empresa.Id, ana, StatusPedidoMapper.AguardandoPagamento, 40m, Agora.AddDays(-1));
        var p4 = NovoPedido(empresa.Id, bia, StatusPedidoMapper.Aguardando, 80m, Agora.AddHours(-1), agendado: Agora.AddHours(2));
        var p5 = NovoPedido(empresa.Id, bia, StatusPedidoMapper.Cancelado, 60m, Agora.AddDays(-3));
        var p6 = NovoPedido(empresa.Id, null, StatusPedidoMapper.Entregue, 30m, Agora.AddHours(-5), pago: 30m, itens: [("Quiche", 5m), (PedidoItem.PrefixoDoFrete + "Centro", 1m)]);
        var p7 = NovoPedido(empresa.Id, bia, StatusPedidoMapper.Entregue, 70m, Agora.AddDays(-20));
        p7.MarcarQuitadoHistorico(Agora.AddDays(-19));
        var p8 = NovoPedido(empresa.Id, null, StatusPedidoMapper.AguardandoAprovacaoBaba, 90m, Agora.AddHours(-4));
        var p9 = NovoPedido(empresa.Id, null, StatusPedidoMapper.Rascunho, 10m, Agora.AddHours(-6), agendado: Agora.AddHours(-5));
        // Alheios: compra antiga apontando a Ana (não pode virar a primeira compra dela) e uma venda grande.
        var alheioDaAna = NovoPedido(outra.Id, ana, StatusPedidoMapper.Entregue, 500m, Agora.AddDays(-30), itens: [("Bolo", 50m)]);
        var alheio = NovoPedido(outra.Id, zeca, StatusPedidoMapper.Preparando, 999m, Agora.AddDays(-1), agendado: Agora.AddHours(-2));

        await using (var gravar = fixture.CreateDbContext())
        {
            using var bypass = gravar.UseRowLevelSecurityBypass();
            await gravar.Database.MigrateAsync();
            gravar.Empresas.AddRange(empresa, outra);
            gravar.Clientes.AddRange(ana, bia, zeca);
            gravar.Pedidos.AddRange(p1, p2, p3, p4, p5, p6, p7, p8, p9, alheioDaAna, alheio);
            await gravar.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var consulta = new PedidosResumoQuery(db);
        var todos = new FiltroDosPedidos(empresa.Id);

        var r = await consulta.ResumirAsync(todos, Agora);

        r.Pedidos.Should().Be(9);
        r.Vendas.Should().Be(6, "cancelado, rascunho e aguardando pagamento não são compra");
        r.Vendido.Should().Be(420m);
        r.AReceberValor.Should().Be(200m, "30 + 80 + 90; o quitado pelo histórico não deve nada");
        r.AReceberPedidos.Should().Be(3);
        r.AguardandoPagamento.Should().Be(1);
        r.AguardandoAprovacao.Should().Be(1);
        r.EmAndamento.Should().Be(2);
        r.Atrasados.Should().Be(1, "o rascunho vencido não conta e o agendado de Bia ainda não venceu");
        r.CanceladosPedidos.Should().Be(1);
        r.CanceladosValor.Should().Be(60m);
        r.PrimeiraCompra.Should().Be(2, "a compra alheia com a Ana não tira a primeira compra dela aqui");
        r.Recompra.Should().Be(2);
        r.MaisVendidos.Select(i => (i.Nome, i.Quantidade)).Should().Equal(("Quiche", 5m), ("Bolo", 3m), ("Torta", 1m));

        var daAna = await consulta.ResumirAsync(todos with { Search = "ana resumo" }, Agora);
        daAna.Pedidos.Should().Be(3);
        daAna.Vendido.Should().Be(150m);

        var recentes = await consulta.VendasAsync(todos with { Desde = Agora.AddDays(-3), Ate = Agora });
        recentes.Should().Be(new VendasDoPeriodo(4, 250m));

        var sinais = await consulta.SinaisAsync(empresa.Id, [p1.Id, p2.Id, p3.Id, p4.Id, p7.Id, alheio.Id]);
        sinais.Should().NotContainKey(alheio.Id, "pedido de outra empresa não entra nem pelo id");
        sinais[p1.Id].CompraDoCliente.Should().Be(1);
        sinais[p2.Id].CompraDoCliente.Should().Be(2);
        sinais[p3.Id].CompraDoCliente.Should().BeNull("aguardando pagamento não é compra");
        sinais[p7.Id].CompraDoCliente.Should().Be(1);
        sinais[p4.Id].CompraDoCliente.Should().Be(2);
        sinais[p2.Id].SituacaoDesde.Should().BeCloseTo(Agora.AddHours(-3), TimeSpan.FromMilliseconds(1));
        sinais[p1.Id].SituacaoDesde.Should().BeCloseTo(p1.CriadoEm, TimeSpan.FromMilliseconds(1));

        // A faixa bate com as linhas: as três páginas da lista somam o mesmo vendido e a mesma quantidade.
        var repo = new PedidoRepository(db);
        var linhas = new List<Pedido>();
        for (var pagina = 1; pagina <= 3; pagina++) linhas.AddRange((await repo.GetByEmpresaAsync(empresa.Id, pagina, 4)).items);
        linhas.Should().HaveCount(r.Pedidos);
        linhas.Where(p => RegraDeCompra.EhCompra(p.Status)).Sum(p => p.Total.Valor).Should().Be(r.Vendido);
    }

    private static string Documento() => Guid.NewGuid().ToString("N")[..14];

    private static Pedido NovoPedido(Guid empresaId, Cliente? cliente, string status, decimal total, DateTime criadoEm,
        decimal pago = 0m, DateTime? agendado = null, (string Nome, decimal Quantidade)[]? itens = null)
    {
        var p = EasyStock.Domain.Entities.Pedido.Criar(empresaId, cliente, origem: "whatsapp");
        p.Status = status;
        p.Total = Dinheiro.FromDecimal(total);
        p.CriadoEm = criadoEm;
        p.AgendadoParaEm = agendado;
        var minuto = 0;
        foreach (var (nome, quantidade) in itens ?? [])
            p.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = p.Id, Nome = nome, Quantidade = quantidade, PrecoUnitario = 1m, Subtotal = quantidade, CriadoEm = criadoEm.AddMinutes(minuto++) });
        if (pago > 0m)
            p.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = p.Id, Metodo = "pix", Valor = pago,
                Referencia = Guid.NewGuid().ToString("N")[..24], PagoEm = criadoEm });
        return p;
    }

    private static PedidoEvento Evento(Pedido p, string statusNovo, DateTime em) =>
        new() { Id = Guid.NewGuid(), PedidoId = p.Id, Tipo = "status_alterado", StatusNovo = statusNovo, OcorridoEm = em };
}
