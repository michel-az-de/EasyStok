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

    private RegistrarProducaoUseCase Sut(RegistrarEntradaEstoqueUseCase? entrada = null, ILojaRepository? lojas = null) => new(
        new CriarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<CriarLoteUseCase>>()),
        new FinalizarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<FinalizarLoteUseCase>>()),
        entrada ?? new RegistrarEntradaEstoqueUseCase(_produtoRepo, _variacaoRepo, _itemRepo, _movRepo, _uow,
            Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(),
            publicadorEventos: Substitute.For<IPublicadorEventos>(),
            loteRepository: _loteRepo),
        _produtoRepo,
        _uow,
        Substitute.For<ILogger<RegistrarProducaoUseCase>>(),
        null,
        lojas,
        _composicao);

    private readonly IProdutoComposicaoRepository _composicao = Substitute.For<IProdutoComposicaoRepository>();

    private Produto Insumo(string nome, UnidadeMedida unidade, decimal? custoPorUnidade)
    {
        var insumo = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = nome, Status = StatusProduto.Ativo, UnidadeMedidaBase = unidade };
        insumo.DefinirCustoDoInsumo(custoPorUnidade);
        return insumo;
    }

    private void Receita(Produto prato, decimal rendimento, UnidadeMedida unidadeDoRendimento,
        params (Produto Insumo, decimal Quantidade, UnidadeMedida Unidade)[] linhas)
    {
        prato.RendimentoBase = rendimento;
        prato.RendimentoUnidade = unidadeDoRendimento;
        IReadOnlyCollection<ProdutoComposicao> receita = linhas.Select(l => new ProdutoComposicao
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoFinalId = prato.Id, InsumoId = l.Insumo.Id,
            Insumo = l.Insumo, Quantidade = l.Quantidade, Unidade = l.Unidade
        }).ToList();
        _composicao.GetByProdutoFinalAsync(_empresaId, prato.Id, null, Arg.Any<CancellationToken>()).Returns(receita);
    }

    private async Task<decimal> CustoDoLoteAsync(Produto prato, int porcoes, int? pesoPorPorcaoG, decimal? custoInformado = null)
    {
        _loteRepo.FindByCodigoAsync(_empresaId, Arg.Any<string>()).Returns(_ => _loteGravado);
        ItemEstoque? entrada = null;
        _itemRepo.When(r => r.InsertAsync(Arg.Any<ItemEstoque>())).Do(c => entrada = c.Arg<ItemEstoque>());
        await Sut().ExecuteAsync(new RegistrarProducaoCommand(_empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(prato.Id, porcoes, pesoPorPorcaoG, ValidadeDias: 3, CustoUnitario: custoInformado)]));
        return entrada!.CustoUnitario.Valor;
    }

    // #1776 (item 4): o lote da produção própria nascia com custo 0, então toda perda valia R$ 0,00 e a regra de
    // perda acima de R$ 50 nunca pedia Gerente. Sem custo informado, a porção leva o custo da receita.
    [Fact]
    public async Task ProducaoSemCustoInformado_LevaOCustoDaPorcaoPelaReceitaEmPorcoes()
    {
        var lasanha = NovoProduto(TipoEmbalagem.Avulso);
        Receita(lasanha, 10m, UnidadeMedida.Un,
            (Insumo("Farinha", UnidadeMedida.G, 0.02m), 1000m, UnidadeMedida.G),
            (Insumo("Queijo", UnidadeMedida.Kg, 30m), 0.5m, UnidadeMedida.Kg));

        (await CustoDoLoteAsync(lasanha, porcoes: 4, pesoPorPorcaoG: null)).Should().Be(3.50m, "R$ 35 da receita ÷ 10 porções");
    }

    [Fact]
    public async Task ProducaoSemCustoInformado_LevaOCustoDaPorcaoPelaReceitaEmPeso()
    {
        var molho = NovoProduto(TipoEmbalagem.Avulso);
        Receita(molho, 2m, UnidadeMedida.Kg, (Insumo("Tomate", UnidadeMedida.Kg, 8m), 5m, UnidadeMedida.Kg));

        (await CustoDoLoteAsync(molho, porcoes: 3, pesoPorPorcaoG: 500)).Should().Be(10m, "R$ 40 rendem 2 kg; a porção de 500 g custa R$ 10");
    }

    [Fact]
    public async Task ReceitaComInsumoSemCusto_NaoInventaCusto_EUsaOCustoDeReferenciaDoPrato()
    {
        // Custo parcial pareceria barato demais (mesma regra da tela de receitas): fica o custo de referência.
        var lasanha = NovoProduto(TipoEmbalagem.Avulso);
        lasanha.CustoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(6m);
        Receita(lasanha, 10m, UnidadeMedida.Un,
            (Insumo("Farinha", UnidadeMedida.G, 0.02m), 1000m, UnidadeMedida.G),
            (Insumo("Queijo", UnidadeMedida.Kg, null), 0.5m, UnidadeMedida.Kg));

        (await CustoDoLoteAsync(lasanha, porcoes: 4, pesoPorPorcaoG: null)).Should().Be(6m);
    }

    [Fact]
    public async Task ReceitaIncompleta_SemCustoDeReferencia_LoteFicaComCustoZero_ESemBloquearAProducao()
    {
        // #1776 (item 4): sem custo conhecido o lote segue em R$ 0 (a perda dele não exige o Gerente), como antes.
        var lasanha = NovoProduto(TipoEmbalagem.Avulso);
        Receita(lasanha, 10m, UnidadeMedida.Un, (Insumo("Queijo", UnidadeMedida.Kg, null), 0.5m, UnidadeMedida.Kg));

        (await CustoDoLoteAsync(lasanha, porcoes: 4, pesoPorPorcaoG: null)).Should().Be(0m);
    }

    // #1776 (item 5): 99.999.999 g por porção era aceito.
    [Fact]
    public async Task PesoPorPorcaoAcimaDoTeto_eRecusado()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);

        var act = () => Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 2, PesoPorPorcaoG: 50_001, ValidadeDias: 3, CustoUnitario: 12m)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*50.000 g*");
    }

    [Fact]
    public async Task CustoInformadoNaProducao_PrevaleceSobreAReceita()
    {
        var lasanha = NovoProduto(TipoEmbalagem.Avulso);
        Receita(lasanha, 10m, UnidadeMedida.Un, (Insumo("Farinha", UnidadeMedida.G, 0.02m), 1000m, UnidadeMedida.G));

        (await CustoDoLoteAsync(lasanha, porcoes: 4, pesoPorPorcaoG: null, custoInformado: 12m)).Should().Be(12m);
    }

    // #2343 (R2-API-7): sem teto, 1.000.000 porcoes geravam 1 etiqueta por unidade e travavam a API.
    [Fact]
    public async Task PorcoesAcimaDoTeto_SaoRecusadasAntesDeGerarEtiquetas()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);

        var act = () => Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 10_000, PesoPorPorcaoG: 500, ValidadeDias: 5, CustoUnitario: 12m)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*porcoes*");
        await _loteRepo.DidNotReceiveWithAnyArgs().AddEtiquetaAsync(default!);
    }

    [Fact]
    public async Task ValidadeAcimaDoTeto_eRecusada()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);

        var act = () => Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 2, PesoPorPorcaoG: 500, ValidadeDias: 3651, CustoUnitario: 12m)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*validade*");
    }

    // #2343 (R2-API-8): loja de outra empresa ou inexistente virava 500 (FK) em vez de 400.
    [Fact]
    public async Task LojaInexistenteNaEmpresa_eRecusadaComoValidacao()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);
        var lojaId = Guid.NewGuid();
        var lojas = Substitute.For<ILojaRepository>();
        lojas.GetByIdAsync(_empresaId, lojaId).Returns((EasyStock.Domain.Entities.Loja?)null);

        var act = () => Sut(lojas: lojas).ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, lojaId, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 2, PesoPorPorcaoG: 500, ValidadeDias: 5, CustoUnitario: 12m)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*encontrada*");
    }

    // #2343 (R2-API-7): o seq de etiqueta e global no lote; o teto vale para a soma das porcoes, nao por item.
    [Fact]
    public async Task SomaDasPorcoesDoLoteAcimaDoTeto_eRecusadaMesmoComItensPequenos()
    {
        var a = NovoProduto(TipoEmbalagem.Embalado);
        var b = NovoProduto(TipoEmbalagem.Embalado);

        var act = () => Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, _dataProducao,
            [
                new RegistrarProducaoItemInput(a.Id, Porcoes: 6_000, PesoPorPorcaoG: 500, ValidadeDias: 5, CustoUnitario: 12m),
                new RegistrarProducaoItemInput(b.Id, Porcoes: 6_000, PesoPorPorcaoG: 500, ValidadeDias: 5, CustoUnitario: 12m),
            ]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*porcoes*");
    }

    [Fact]
    public async Task LojaExistenteNaEmpresa_NaoEhRecusada()
    {
        var produto = NovoProduto(TipoEmbalagem.Embalado);
        var lojaId = Guid.NewGuid();
        var lojas = Substitute.For<ILojaRepository>();
        lojas.GetByIdAsync(_empresaId, lojaId).Returns(EasyStock.Domain.Entities.Loja.Criar(_empresaId, "Centro"));
        _loteRepo.FindByCodigoAsync(_empresaId, Arg.Any<string>()).Returns(_ => _loteGravado);

        var ex = await Record.ExceptionAsync(() => Sut(lojas: lojas).ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, lojaId, _dataProducao,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 2, PesoPorPorcaoG: 500, ValidadeDias: 5, CustoUnitario: 12m)])));

        // A loja existe: a validacao passa (o que vier depois, como a configuracao da loja, nao e deste teste).
        (ex is UseCaseValidationException { Message: var msg } && msg.Contains("encontrada")).Should().BeFalse();
        await lojas.Received(1).GetByIdAsync(_empresaId, lojaId);
    }

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
    public async Task ProducaoDepoisDas21h_ContaValidadeEDiaDoLotePeloDiaDeBrasilia()
    {
        // #1506: 08/10 22:00 em Brasilia = 09/10 01:00Z. Somar dias ao instante UTC gravava a
        // validade com um dia a mais (risco sanitario) e o lote como LOT-261009.
        var produto = NovoProduto(TipoEmbalagem.Embalado);
        _loteRepo.FindByCodigoAsync(_empresaId, Arg.Any<string>()).Returns(_ => _loteGravado);
        var noite = new DateTime(2026, 10, 9, 1, 0, 0, DateTimeKind.Utc);

        var result = await Sut().ExecuteAsync(new RegistrarProducaoCommand(
            _empresaId, null, noite,
            [new RegistrarProducaoItemInput(produto.Id, Porcoes: 1, PesoPorPorcaoG: 500, ValidadeDias: 3, CustoUnitario: 12m)]));

        await _itemRepo.Received(1).InsertAsync(Arg.Is<ItemEstoque>(i =>
            i.ValidadeEm != null && i.ValidadeEm.DataValidade == new DateTime(2026, 10, 11)));
        result.Itens.Should().ContainSingle(i => i.ValidadeEm == new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));
        await _loteRepo.Received().GetNextSequencialDoDiaAsync(_empresaId, new DateOnly(2026, 10, 8));
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
