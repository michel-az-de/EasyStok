using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Regressao #926/#933 — o mesmo dinheiro nao pode contar 2x no caixa.
///
/// <para>
/// O saldo do caixa (CaixaSaldoCalculator) e
/// <c>saldoInicial + totalVendas + totalPagamentosPedidos + entradas - saidas</c>.
/// Um pedido mobile entregue vira uma <see cref="Venda"/> (VendaId setado) e o valor
/// ja entra em <c>GetTotalVendas</c>; se o <see cref="PedidoPagamento"/> desse pedido
/// tambem entrasse em <c>GetTotalPagamentosPedidos</c>, o mesmo dinheiro somaria duas
/// vezes. O fix filtra <c>p.VendaId == null</c> nas duas queries (total e lista).
/// </para>
///
/// <para>Estes testes travam o comportamento contra regressao:</para>
/// <list type="bullet">
///   <item>o total de pagamentos-pedidos exclui pedidos ja consolidados em Venda (o 2x);</item>
///   <item>pedido cancelado continua fora (o filtro pre-existente nao regrediu);</item>
///   <item>a lista de linhas exibidas casa 1:1 com o total (soma das linhas == total).</item>
/// </list>
///
/// <para>
/// Nota (verificado ao escrever o teste): <c>GetTotalVendasNoIntervaloAsync</c> NAO filtra
/// venda cancelada — a entidade <see cref="Venda"/> nao tem campo de status/cancelamento.
/// Logo o valor de um pedido consolidado aparece SEMPRE exatamente uma vez (via Venda),
/// nunca some; por isso nao ha caso "dinheiro desaparece no estorno" a cobrir aqui.
/// </para>
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CaixaRepositoryPagamentosPedidoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Pagamento_de_pedido_consolidado_em_venda_nao_conta_no_total_evitando_2x()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(NovaEmpresa(empresaId));

        var agora = DateTime.UtcNow;
        var ini = agora.AddHours(-1);
        var fim = agora.AddHours(1);

        // Pedido A — balcao/web: SEM Venda (VendaId null). DEVE contar em pagamentos-pedidos.
        var pedidoBalcao = NovoPedido(empresaId, status: StatusPedidoMapper.Aguardando, vendaId: null);
        db.Pedidos.Add(pedidoBalcao);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedidoBalcao.Id, 10m, agora));

        // Pedido B — mobile entregue -> consolidado numa Venda (VendaId setado). O ValorTotal
        // da Venda ja aparece em GetTotalVendas; o PedidoPagamento NAO pode contar de novo.
        var vendaConsolidada = NovaVenda(empresaId, valor: 25m, dataVenda: agora);
        db.Vendas.Add(vendaConsolidada);
        var pedidoMobile = NovoPedido(empresaId, status: StatusPedidoMapper.Entregue, vendaId: vendaConsolidada.Id);
        db.Pedidos.Add(pedidoMobile);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedidoMobile.Id, 25m, agora));

        // Pedido C — cancelado (VendaId null): fica fora por outro filtro; garante que nao regrediu.
        var pedidoCancelado = NovoPedido(empresaId, status: StatusPedidoMapper.Cancelado, vendaId: null);
        db.Pedidos.Add(pedidoCancelado);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedidoCancelado.Id, 99m, agora));

        await db.SaveChangesAsync();

        var repo = new CaixaRepository(db);
        var totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosNoIntervaloAsync(empresaId, ini, fim);
        var totalVendas = await repo.GetTotalVendasNoIntervaloAsync(empresaId, ini, fim);

        // So o pedido balcao entra. Sem o fix, o mobile somaria +25 (total 35) e, com o cancelado,
        // chegaria a 134 — o cancelado ja era filtrado antes; o mobile e o que o #926/#933 corrige.
        totalPagamentosPedidos.Should().Be(10m,
            "pagamento de pedido consolidado em Venda (mobile) e de pedido cancelado nao entram (#926/#933)");
        totalVendas.Should().Be(25m,
            "a Venda consolidada do pedido mobile ja representa esse dinheiro uma vez");

        // Invariante do caixa: o dinheiro do pedido mobile aparece UMA vez (via Venda), nao duas.
        // Antes do fix esta parcela do saldo era 60 (25 vendas + 35 pagamentos); agora e 35.
        (totalVendas + totalPagamentosPedidos).Should().Be(35m,
            "o mesmo dinheiro do pedido mobile nao pode contar 2x no caixa (#926)");
    }

    [SkippableFact]
    public async Task Lista_de_pagamentos_exclui_pedido_consolidado_e_soma_bate_com_o_total()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(NovaEmpresa(empresaId));

        var agora = DateTime.UtcNow;
        var ini = agora.AddHours(-1);
        var fim = agora.AddHours(1);

        var pedidoBalcao = NovoPedido(empresaId, status: StatusPedidoMapper.Aguardando, vendaId: null);
        db.Pedidos.Add(pedidoBalcao);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedidoBalcao.Id, 40m, agora));

        var venda = NovaVenda(empresaId, valor: 70m, dataVenda: agora);
        db.Vendas.Add(venda);
        var pedidoMobile = NovoPedido(empresaId, status: StatusPedidoMapper.Entregue, vendaId: venda.Id);
        db.Pedidos.Add(pedidoMobile);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedidoMobile.Id, 70m, agora));

        await db.SaveChangesAsync();

        var repo = new CaixaRepository(db);
        var linhas = await repo.GetPagamentosPedidosListaNoIntervaloAsync(empresaId, ini, fim);
        var total = await repo.GetTotalPagamentosPedidosNoIntervaloAsync(empresaId, ini, fim);

        // A lista exibida nao pode trazer o pagamento do pedido ja consolidado em Venda.
        linhas.Should().ContainSingle(pg => pg.PedidoId == pedidoBalcao.Id);
        linhas.Should().NotContain(pg => pg.PedidoId == pedidoMobile.Id);

        // Invariante da tela do caixa: soma das linhas exibidas == total somado ao saldo.
        linhas.Sum(pg => pg.Valor).Should().Be(total);
        total.Should().Be(40m);
    }

    private static Empresa NovaEmpresa(Guid empresaId) => new()
    {
        Id = empresaId,
        Nome = "Empresa Caixa",
        Documento = empresaId.ToString("N")[..14],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow
    };

    private static Pedido NovoPedido(Guid empresaId, string status, Guid? vendaId)
    {
        var agora = DateTime.UtcNow;
        return new Pedido
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Status = status,
            VendaId = vendaId,
            Total = Dinheiro.Zero,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    private static Venda NovaVenda(Guid empresaId, decimal valor, DateTime dataVenda) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        Canal = CanalVenda.LojaPropria,
        Natureza = NaturezaMovimentacaoEstoque.Venda,
        DataVenda = dataVenda,
        ValorTotal = Dinheiro.FromDecimal(valor),
        CriadoEm = dataVenda
    };

    private static PedidoPagamento NovoPagamento(Guid pedidoId, decimal valor, DateTime pagoEm) => new()
    {
        Id = Guid.NewGuid(),
        PedidoId = pedidoId,
        Metodo = "dinheiro",
        Valor = valor,
        PagoEm = pagoEm
    };
}
