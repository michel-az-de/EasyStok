using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.FinalizarLote;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Producao;

/// <summary>S23 (#1137): producao em porcoes cria Lote finalizado + entrada de estoque na mesma transacao.</summary>
public class RegistrarProducaoUseCaseTests
{
    private readonly ILoteRepository _loteRepo = Substitute.For<ILoteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IProdutoVariacaoRepository _variacaoRepo = Substitute.For<IProdutoVariacaoRepository>();
    private readonly IItemEstoqueRepository _itemRepo = Substitute.For<IItemEstoqueRepository>();
    private readonly IMovimentacaoEstoqueRepository _movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly DateTime _dataProducao = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
    private Lote? _loteGravado;

    public RegistrarProducaoUseCaseTests()
    {
        _uow.SetupExecuteInTransactionSemRetry<RegistrarProducaoResult>();
        _loteRepo.GetNextSequencialDoDiaAsync(_empresaId, Arg.Any<DateOnly>()).Returns(1);
        _loteRepo.When(r => r.AddAsync(Arg.Any<Lote>())).Do(c => _loteGravado = c.Arg<Lote>());
        _loteRepo.GetByIdWithDetailsAsync(_empresaId, Arg.Any<Guid>()).Returns(_ => _loteGravado);
    }

    private Produto NovoProduto(TipoEmbalagem tipo)
    {
        var p = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            Nome = "Lasanha 500 g",
            Status = StatusProduto.Ativo,
            TipoEmbalagem = tipo
        };
        _produtoRepo.GetByIdAsync(p.Id).Returns(p);
        _produtoRepo.GetByIdAsync(_empresaId, p.Id).Returns(p);
        _produtoRepo.GetTipoEmbalagemMapAsync(_empresaId, Arg.Any<IEnumerable<Guid>>())
            .ReturnsForAnyArgs(new Dictionary<Guid, TipoEmbalagem> { [p.Id] = tipo });
        return p;
    }

    private RegistrarProducaoUseCase Sut(RegistrarEntradaEstoqueUseCase? entrada = null) => new(
        new CriarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<CriarLoteUseCase>>()),
        new FinalizarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<FinalizarLoteUseCase>>()),
        entrada ?? new RegistrarEntradaEstoqueUseCase(_produtoRepo, _variacaoRepo, _itemRepo, _movRepo, _uow,
            Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(),
            publicadorEventos: Substitute.For<IPublicadorEventos>(),
            loteRepository: _loteRepo),
        _produtoRepo,
        _uow,
        Substitute.For<ILogger<RegistrarProducaoUseCase>>());

    [Fact]
    public async Task CriaLoteEEstoqueNaMesmaTransacao()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);
        _loteRepo.FindByCodigoAsync(_empresaId, Arg.Any<string>()).Returns(_ => _loteGravado);

        var result = await Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 2, PesoPorPorcaoG: 500, ValidadeDias: 5, CustoUnitario: 12m)]));

        await _uow.Received(1).ExecuteInTransactionSemRetryAsync(
            Arg.Any<Func<CancellationToken, Task<RegistrarProducaoResult>>>(), Arg.Any<CancellationToken>());
        _loteGravado.Should().NotBeNull();
        _loteGravado!.EstaFinalizado.Should().BeTrue();
        _loteGravado.Etiquetas.Should().HaveCount(2);
        _loteGravado.Itens.Should().ContainSingle(i => i.Quantidade == 2 && i.PesoG == 500 && i.ValidadeDias == 5);
        result.CodigoLote.Should().Be(_loteGravado.Codigo);
        result.TotalEtiquetas.Should().Be(2);

        await _itemRepo.Received(1).InsertAsync(Arg.Is<ItemEstoque>(i =>
            i.QuantidadeAtual.Value == 2 &&
            i.ValidadeEm != null && i.ValidadeEm.DataValidade == _dataProducao.AddDays(5).Date &&
            i.CodigoLote != null && i.CodigoLote.Value == _loteGravado.Codigo));
        await _movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.Tipo == TipoMovimentacaoEstoque.Entrada && m.Natureza == NaturezaMovimentacaoEstoque.Producao));
        // Entrada nao duplica o item no lote ja finalizado.
        await _loteRepo.DidNotReceive().AddItemAsync(Arg.Any<LoteItem>());
    }

    [Fact]
    public async Task FalhaDesfazTudo()
    {
        var produto = NovoProduto(TipoEmbalagem.Avulso);
        var entrada = Substitute.ForPartsOf<RegistrarEntradaEstoqueUseCase>(_produtoRepo, _variacaoRepo, _itemRepo, _movRepo, _uow,
            Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(), null, null, null, null, null);
        entrada.ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>())
            .Returns<Task<RegistrarEntradaEstoqueResult>>(_ => throw new InvalidOperationException("falha na entrada"));

        var act = () => Sut(entrada).ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, 2, null, 5, null)]));

        // A excecao atravessa o ExecuteInTransactionSemRetryAsync: a transacao do provider faz rollback
        // do lote (o rollback real contra Postgres e coberto em RegistrarProducaoIntegrationTests).
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _uow.Received(1).ExecuteInTransactionSemRetryAsync(
            Arg.Any<Func<CancellationToken, Task<RegistrarProducaoResult>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbaladoSemPesoRejeita()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);

        var act = () => Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, 2, null, 5, null)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*peso*");
        await _uow.DidNotReceiveWithAnyArgs().ExecuteInTransactionSemRetryAsync<RegistrarProducaoResult>(default!, default);
        await _loteRepo.DidNotReceive().AddAsync(Arg.Any<Lote>());
    }

    // M2.2 (#1491, US-061): o peso real fecha as porções; o que passa é sobra.
    [Theory]
    [InlineData(1000, 0)]
    [InlineData(1144, 144)]
    public async Task PesoReal_DaASobraDaProducao(int pesoReal, int sobra)
    {
        var produto = NovoProduto(TipoEmbalagem.Avulso);
        _loteRepo.FindByCodigoAsync(_empresaId, Arg.Any<string>()).Returns(_ => _loteGravado);

        var r = await Sut().ExecuteAsync(new RegistrarProducaoCommand(_empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, 2, 500, 5, null, pesoReal)]));

        r.Itens.Single().SobraG.Should().Be(sobra);
        _loteGravado!.Itens.Single().PesoRealG.Should().Be(pesoReal);
    }

    [Fact]
    public async Task PesoRealMenorQueAsPorcoes_400()
    {
        var produto = NovoProduto(TipoEmbalagem.Avulso);

        var act = () => Sut().ExecuteAsync(new RegistrarProducaoCommand(_empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, 2, 500, 5, null, 900)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*peso real*");
    }
}
