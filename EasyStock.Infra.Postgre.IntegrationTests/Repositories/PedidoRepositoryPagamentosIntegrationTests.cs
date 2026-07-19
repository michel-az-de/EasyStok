using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// issue 958 (BUG-003+002 do QA): <c>GetByEmpresaAsync</c> (listagem/cockpit) nao carregava
/// <see cref="Pedido.Pagamentos"/> — <see cref="Pedido.TotalPago"/> e computed property que
/// soma essa colecao em memoria, entao a listagem sempre lia 0 e divergia do detalhe
/// (<c>GetByIdWithDetailsAsync</c>, que ja tinha o Include correto).
///
/// <para>Estes testes travam o invariante contra regressao: qualquer caminho de leitura de
/// pedido que exponha <c>TotalPago</c>/status de pagamento tem que bater entre si.</para>
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PedidoRepositoryPagamentosIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Listagem_e_detalhe_reportam_o_mesmo_TotalPago()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(NovaEmpresa(empresaId));

        var pedido = NovoPedidoComItem(empresaId, totalItem: 299.40m);
        db.Pedidos.Add(pedido);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedido.Id, 299.40m));
        await db.SaveChangesAsync();

        var repo = new PedidoRepository(db);

        var (itensListagem, _) = await repo.GetByEmpresaAsync(empresaId, page: 1, pageSize: 50);
        var daListagem = itensListagem.Single(p => p.Id == pedido.Id);

        var doDetalhe = await repo.GetByIdWithDetailsAsync(empresaId, pedido.Id);

        daListagem.TotalPago.Should().Be(doDetalhe!.TotalPago,
            "listagem e detalhe do MESMO pedido nao podem divergir de status de pagamento (BUG-003)");
        daListagem.TotalPago.Should().Be(299.40m);
    }

    [SkippableFact]
    public async Task GetByEmpresaAsync_carrega_pagamentos_e_reflete_no_TotalPago()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(NovaEmpresa(empresaId));

        var pedido = NovoPedidoComItem(empresaId, totalItem: 76m);
        db.Pedidos.Add(pedido);
        db.Set<PedidoPagamento>().Add(NovoPagamento(pedido.Id, 76m));
        await db.SaveChangesAsync();

        var repo = new PedidoRepository(db);
        var (itens, _) = await repo.GetByEmpresaAsync(empresaId, page: 1, pageSize: 50);

        // Sem o Include (regressao), isto daria 0m mesmo com o pagamento persistido.
        itens.Single(p => p.Id == pedido.Id).TotalPago.Should().Be(76m);
    }

    [SkippableFact]
    public async Task GetByEmpresaAsync_pedido_sem_pagamento_mantem_TotalPago_zero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(NovaEmpresa(empresaId));

        var pedido = NovoPedidoComItem(empresaId, totalItem: 50m);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        var repo = new PedidoRepository(db);
        var (itens, _) = await repo.GetByEmpresaAsync(empresaId, page: 1, pageSize: 50);

        // Guarda contra falso-positivo: o Include nao pode inventar pagamento que nao existe.
        itens.Single(p => p.Id == pedido.Id).TotalPago.Should().Be(0m);
    }

    private static Empresa NovaEmpresa(Guid empresaId) => new()
    {
        Id = empresaId,
        Nome = "Empresa Pedidos Pagamentos",
        Documento = empresaId.ToString("N")[..14],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow
    };

    private static Pedido NovoPedidoComItem(Guid empresaId, decimal totalItem)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Nome = "Item",
            Quantidade = 1,
            PrecoUnitario = totalItem,
            Subtotal = totalItem,
            CriadoEm = DateTime.UtcNow
        });
        pedido.RecalcularTotal();
        return pedido;
    }

    private static PedidoPagamento NovoPagamento(Guid pedidoId, decimal valor) => new()
    {
        Id = Guid.NewGuid(),
        PedidoId = pedidoId,
        Metodo = "pix",
        Valor = valor,
        PagoEm = DateTime.UtcNow
    };
}
