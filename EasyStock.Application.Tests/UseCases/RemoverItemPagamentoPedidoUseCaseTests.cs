using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.RemoverItemPedido;
using EasyStock.Application.UseCases.RemoverPagamentoPedido;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #768: a remoção de item/pagamento passou a ser rastreada (pedido.Itens.Remove /
/// pedido.Pagamentos.Remove) em vez de ExecuteDeleteAsync imediato fora do UoW.
/// O DELETE participa do mesmo SaveChanges do recálculo do total + evento (atômico).
/// </summary>
public class RemoverItemPagamentoPedidoUseCaseTests
{
    private readonly IPedidoRepository _repo = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly EasyStock.Application.Ports.Output.Persistence.Pagamentos.ICobrancaPedidoRepository _cobrancas =
        Substitute.For<EasyStock.Application.Ports.Output.Persistence.Pagamentos.ICobrancaPedidoRepository>();

    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();

    public RemoverItemPagamentoPedidoUseCaseTests() =>
        _uow.SetupExecuteInTransactionSemRetry<EasyStock.Application.UseCases.Pedidos.PedidoResult?>();

    private RemoverItemPedidoUseCase ItemUC() =>
        new(_repo,
            new PedidoEstoqueIntegrationService(
                Substitute.For<IItemEstoqueRepository>(),
                Substitute.For<IMovimentacaoEstoqueRepository>(),
                Substitute.For<EasyStock.Application.Ports.Output.Integration.IPublicadorEventoIntegracao>(),
                Microsoft.Extensions.Options.Options.Create(new PedidoEstoqueOptions()),
                Substitute.For<ILogger<PedidoEstoqueIntegrationService>>()),
            _uow, Substitute.For<ILogger<RemoverItemPedidoUseCase>>());
    private RemoverPagamentoPedidoUseCase PagUC() =>
        new(_repo, _cobrancas, _uow,
            Substitute.For<ILogger<RemoverPagamentoPedidoUseCase>>(), QuitacaoPedidoTeste.Criar(_repo), _caixa);

    private static PedidoItem NovoItem(Guid pedidoId, string nome, decimal qtd, decimal preco) => new()
    {
        Id = Guid.NewGuid(), PedidoId = pedidoId, Nome = nome,
        Quantidade = qtd, PrecoUnitario = preco, Subtotal = qtd * preco, CriadoEm = DateTime.UtcNow
    };

    [Fact]
    public async Task RemoverItem_TiraDaColecao_RecalculaTotal_E_GravaEvento_SemDeleteImediato()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var itemA = NovoItem(pedido.Id, "A", 2, 10m);   // 20
        var itemB = NovoItem(pedido.Id, "B", 1, 50m);   // 50
        pedido.Itens.Add(itemA);
        pedido.Itens.Add(itemB);
        pedido.RecalcularTotal();
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        PedidoEvento? evento = null;
        await _repo.AddEventoAsync(Arg.Do<PedidoEvento>(e => evento = e));

        await ItemUC().ExecuteAsync(new RemoverItemPedidoCommand(empresaId, pedido.Id, itemA.Id));

        pedido.Itens.Should().ContainSingle(i => i.Id == itemB.Id);
        pedido.Itens.Should().NotContain(i => i.Id == itemA.Id);   // remoção rastreada => Deleted no SaveChanges
        pedido.Total.Valor.Should().Be(50m);                        // recalculado na mesma unidade
        evento!.Tipo.Should().Be("item_removed");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task RemoverPagamento_TiraDaColecao_E_GravaEvento_NaMesmaUnidade()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 30m, PagoEm = DateTime.UtcNow
        };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        PedidoEvento? evento = null;
        await _repo.AddEventoAsync(Arg.Do<PedidoEvento>(e => evento = e));

        await PagUC().ExecuteAsync(new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id));

        pedido.Pagamentos.Should().BeEmpty();
        evento!.Tipo.Should().Be("pagamento_removido");
        await _uow.Received(1).CommitAsync();
    }

    // #1547: o estorno manual recusava com o caixa fechado; remover o mesmo recebimento passava e o
    // fechamento do dia ficava com um total que o Caixa não soma mais.
    [Fact]
    public async Task RemoverPagamento_DeDiaComCaixaFechado_Recusa_NaLojaDoPedido()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.LojaId = Guid.NewGuid();
        // 02:30 UTC de 09/10 ainda é 08/10 em Brasília: o dia da guarda é o operacional do recebimento.
        var pagoEm = new DateTime(2026, 10, 9, 2, 30, 0, DateTimeKind.Utc);
        var pag = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "dinheiro", Valor = 30m, PagoEm = pagoEm };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        var dia = new DateOnly(2026, 10, 8);
        _caixa.GetFechamentoDoDiaAsync(empresaId, dia, pedido.LojaId)
            .Returns(FechamentoCaixa.Criar(empresaId, dia, 0, 0, 30, 0, 0, pedido.LojaId));

        var act = () => PagUC().ExecuteAsync(new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id,
            NivelSolicitante: NivelAcesso.Admin));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*já foi fechado*devolução*");
        pedido.Pagamentos.Should().ContainSingle();
        await _uow.DidNotReceive().CommitAsync();
    }

    // #1547: recebimento lançado DEPOIS de o Caixa fechar não entrou no retrato do dia e segue removível.
    [Fact]
    public async Task RemoverPagamento_LancadoDepoisDoFechamento_ContinuaRemovivel()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var pagoEm = new DateTime(2026, 10, 8, 23, 30, 0, DateTimeKind.Utc);
        var pag = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "dinheiro", Valor = 30m, PagoEm = pagoEm };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        var dia = new DateOnly(2026, 10, 8);
        var fechamento = FechamentoCaixa.Criar(empresaId, dia, 0, 0, 0, 0, 0, pedido.LojaId);
        fechamento.FechadoEm = pagoEm.AddMinutes(-30);
        _caixa.GetFechamentoDoDiaAsync(empresaId, dia, pedido.LojaId).Returns(fechamento);

        await PagUC().ExecuteAsync(new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id,
            NivelSolicitante: NivelAcesso.Admin));

        pedido.Pagamentos.Should().BeEmpty();
        await _uow.Received(1).CommitAsync();
    }

    // #1540: a rota antiga de remoção não tinha regra de dinheiro nenhuma.
    [Fact]
    public async Task RemoverPagamento_DoMercadoPago_Recusa_PorqueSeDevolvePeloEstorno()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 30m, PagoEm = DateTime.UtcNow, Referencia = "pay-9"
        };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        var cobranca = EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.CriarOnline(empresaId, pedido.Id, 30m, "pref-1", "https://mp.test/pref-1",
            DateTime.UtcNow.AddMinutes(30), 1, DateTime.UtcNow);
        cobranca.MarcarPaga("pay-9", 30m, "pix", DateTime.UtcNow);
        _cobrancas.ListarDoPedidoAsync(empresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(new List<EasyStock.Domain.Entities.Pagamentos.CobrancaPedido> { cobranca });

        var act = () => PagUC().ExecuteAsync(new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*estorno*");
        pedido.Pagamentos.Should().ContainSingle();
        await _uow.DidNotReceive().CommitAsync();
    }

    // O nome vem de quem registrou e não prova nada: sem cobrança online por trás, o pagamento é manual
    // e continua removível (senão um nome falsificado criaria recebimento sem saída nenhuma).
    [Fact]
    public async Task RemoverPagamento_ComNomeMercadoPagoMasSemCobrancaOnline_Remove()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 30m, PagoEm = DateTime.UtcNow,
            RegistradoPorNome = "Mercado Pago"
        };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        await PagUC().ExecuteAsync(new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id));

        pedido.Pagamentos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NivelAcesso.Operador, false)]
    [InlineData(NivelAcesso.Gerente, true)]
    public async Task RemoverPagamento_ComPreparoIniciado_SoAGestao(NivelAcesso nivel, bool remove)
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = EasyStock.Domain.Sales.StatusPedidoMapper.Preparando;
        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "dinheiro", Valor = 80m, PagoEm = DateTime.UtcNow
        };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var act = () => PagUC().ExecuteAsync(
            new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id, NivelSolicitante: nivel));

        if (remove)
        {
            await act.Should().NotThrowAsync();
            pedido.Pagamentos.Should().BeEmpty();
        }
        else
        {
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            pedido.Pagamentos.Should().ContainSingle();
        }
    }

    /// <summary>
    /// #1724-5: a comanda do cliente confere o valor recebido antes de mexer nos itens; o DELETE direto do item
    /// não conferia e deixava o pedido com total menor que o recebido, sem devolução.
    /// </summary>
    [Fact]
    public async Task RemoverItem_ComValorRecebido_Recusa()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var item = NovoItem(pedido.Id, "A", 1, 30m);
        pedido.Itens.Add(item);
        pedido.RecalcularTotal();
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Valor = 10m, Metodo = "pix", PagoEm = DateTime.UtcNow });
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var act = () => ItemUC().ExecuteAsync(new RemoverItemPedidoCommand(empresaId, pedido.Id, item.Id));

        await act.Should().ThrowAsync<CobrancaPedidoConflitoException>().WithMessage("*valor recebido*");
        pedido.Itens.Should().ContainSingle();
        await _uow.DidNotReceive().CommitAsync();
    }
}
