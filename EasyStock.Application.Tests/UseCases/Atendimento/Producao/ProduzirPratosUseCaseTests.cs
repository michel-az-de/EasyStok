using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.FinalizarLote;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

/// <summary>
/// M2.2 (#1491): produção pelo prato do cardápio. O prato avulso ganha um produto de estoque na
/// primeira produção e fica ligado a ele; depois é o caminho do S23 (lote, etiquetas, entrada).
/// </summary>
public class ProduzirPratosUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly ICategoriaRepository _categorias = Substitute.For<ICategoriaRepository>();
    private readonly ILoteRepository _loteRepo = Substitute.For<ILoteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<Categoria> _categoriasGravadas = [];
    private readonly Dictionary<Guid, Produto> _produtos = [];
    private Lote? _lote;
    // M1.4c (#1537): variações do estoque e as entradas gravadas, para conferir o saldo por porção.
    private readonly IProdutoVariacaoRepository _variacoes = Substitute.For<IProdutoVariacaoRepository>();
    private readonly IItemEstoqueRepository _itensEstoque = Substitute.For<IItemEstoqueRepository>();
    private readonly List<ProdutoVariacao> _variacoesGravadas = [];
    private readonly List<ItemEstoque> _entradas = [];

    public ProduzirPratosUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _categorias.GetByEmpresaAsync(EmpresaId).Returns(_ => _categoriasGravadas.ToList());
        _categorias.When(c => c.AddAsync(Arg.Any<Categoria>())).Do(c => _categoriasGravadas.Add(c.Arg<Categoria>()));
        _categorias.GetByIdAsync(EmpresaId, Arg.Any<Guid>()).Returns(c => _categoriasGravadas.FirstOrDefault(x => x.Id == c.ArgAt<Guid>(1)));
        _categorias.GetByIdAsync(Arg.Any<Guid>()).Returns(c => _categoriasGravadas.FirstOrDefault(x => x.Id == c.Arg<Guid>()));
        _produtoRepo.When(r => r.InsertAsync(Arg.Any<Produto>())).Do(c => _produtos[c.Arg<Produto>().Id] = c.Arg<Produto>());
        _produtoRepo.GetByIdAsync(EmpresaId, Arg.Any<Guid>()).Returns(c => _produtos.GetValueOrDefault(c.ArgAt<Guid>(1)));
        _produtoRepo.GetByIdAsync(Arg.Any<Guid>()).Returns(c => _produtos.GetValueOrDefault(c.Arg<Guid>()));
        _produtoRepo.GetTipoEmbalagemMapAsync(EmpresaId, Arg.Any<IEnumerable<Guid>>())
            .ReturnsForAnyArgs(new Dictionary<Guid, TipoEmbalagem>());
        _uow.SetupExecuteInTransactionSemRetry<RegistrarProducaoResult>();
        _loteRepo.GetNextSequencialDoDiaAsync(EmpresaId, Arg.Any<DateOnly>()).Returns(1);
        _loteRepo.When(r => r.AddAsync(Arg.Any<Lote>())).Do(c => _lote = c.Arg<Lote>());
        _loteRepo.GetByIdWithDetailsAsync(EmpresaId, Arg.Any<Guid>()).Returns(_ => _lote);
        _loteRepo.FindByCodigoAsync(EmpresaId, Arg.Any<string>()).Returns(_ => _lote);
        _variacoes.When(v => v.InsertAsync(Arg.Any<ProdutoVariacao>())).Do(c => _variacoesGravadas.Add(c.Arg<ProdutoVariacao>()));
        _variacoes.GetByProdutoAsync(EmpresaId, Arg.Any<Guid>()).Returns(c => _variacoesGravadas.Where(v => v.ProdutoId == c.ArgAt<Guid>(1)).ToList());
        _variacoes.GetByIdAsync(Arg.Any<Guid>()).Returns(c => _variacoesGravadas.FirstOrDefault(v => v.Id == c.Arg<Guid>()));
        _itensEstoque.When(i => i.InsertAsync(Arg.Any<ItemEstoque>())).Do(c => _entradas.Add(c.Arg<ItemEstoque>()));
    }

    private ProduzirPratosUseCase Sut()
    {
        var variacoes = _variacoes;
        var registrar = new RegistrarProducaoUseCase(
            new CriarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<CriarLoteUseCase>>()),
            new FinalizarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<FinalizarLoteUseCase>>()),
            new RegistrarEntradaEstoqueUseCase(_produtoRepo, variacoes, _itensEstoque,
                Substitute.For<IMovimentacaoEstoqueRepository>(), _uow, Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(),
                publicadorEventos: Substitute.For<IPublicadorEventos>(), loteRepository: _loteRepo),
            _produtoRepo, _uow, Substitute.For<ILogger<RegistrarProducaoUseCase>>());
        var cadastrar = new CadastrarProdutoUseCase(_produtoRepo, _categorias,
            Substitute.For<IProdutoCaracteristicaRepository>(), Substitute.For<IProdutoEmbalagemRepository>(), variacoes,
            _uow, Substitute.For<ILogger<CadastrarProdutoUseCase>>());
        return new ProduzirPratosUseCase(_storefronts, _cardapio, _categorias, cadastrar, registrar, _uow,
            new EasyStock.Application.UseCases.Atendimento.Comanda.VincularPorcoesAoEstoqueUseCase(_variacoes));
    }

    private (CardapioItem Prato, CardapioItemVariacao P300, CardapioItemVariacao P800) AvulsoComPorcoes(string nome)
    {
        var item = Avulso(nome);
        var p300 = CardapioItemVariacao.Criar(item.Id, "300 g", 28m, ordemExibicao: 0, ehPadrao: true);
        var p800 = CardapioItemVariacao.Criar(item.Id, "800 g", 62m, ordemExibicao: 1);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);
        return (item, p300, p800);
    }

    [Fact]
    public async Task PorcaoProduzida_EntraNoSaldoDela_EOPratoAvulsoGanhaOVinculoDasPorcoes()
    {
        // M1.4c (#1537, D-M1-03): 6 de 800 g e 4 de 300 g no mesmo lote, cada uma no seu saldo.
        var (ravioli, p300, p800) = AvulsoComPorcoes("Ravióli");

        var r = await Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null,
        [
            new PratoProduzidoInput(ravioli.Id, 6, 800, null, 5, p800.Id),
            new PratoProduzidoInput(ravioli.Id, 4, 300, null, 5, p300.Id),
        ]));

        _produtos.Should().HaveCount(1, "as duas porções são do mesmo prato");
        (p300.ProdutoVariacaoId, p800.ProdutoVariacaoId).Should().NotBe((null, null));
        _entradas.Select(e => (e.ProdutoVariacaoId, e.QuantidadeAtual.Value)).Should().BeEquivalentTo(
            [(p800.ProdutoVariacaoId, 6m), (p300.ProdutoVariacaoId, 4m)]);
        r.Pratos.Select(p => p.Nome).Should().Equal("Ravióli 800 g", "Ravióli 300 g");
        _lote!.Itens.Select(i => i.Nome).Should().Equal("Ravióli 800 g", "Ravióli 300 g");
    }

    [Fact]
    public async Task PratoComPorcoes_SemPorcaoEscolhida_NaoProduz()
    {
        var (ravioli, _, _) = AvulsoComPorcoes("Ravióli");

        var act = () => Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null, [new PratoProduzidoInput(ravioli.Id, 6, 800, null, 5)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Escolha a porção*");
        _entradas.Should().BeEmpty();
    }

    [Fact]
    public async Task MesmaPorcaoEmDuasLinhas_Recusa_EPratoSemPorcoesComPorcao_Recusa()
    {
        var (ravioli, p300, _) = AvulsoComPorcoes("Ravióli");
        var duasVezes = () => Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null,
            [new PratoProduzidoInput(ravioli.Id, 2, 300, null, 5, p300.Id), new PratoProduzidoInput(ravioli.Id, 1, 300, null, 5, p300.Id)]));
        await duasVezes.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*duas vezes*");

        var bolo = Avulso("Bolo");
        var semPorcoes = () => Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null,
            [new PratoProduzidoInput(bolo.Id, 2, null, null, 3, Guid.NewGuid())]));
        await semPorcoes.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*não tem essa porção*");
    }

    private CardapioItem Avulso(string nome)
    {
        var item = CardapioItem.CriarAvulso(_vitrine.Id, nome, 45m);
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, item.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task PratoAvulso_GanhaProdutoNaCategoriaCardapio_EProduz()
    {
        var ravioli = Avulso("Ravióli de queijo");

        var r = await Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null,
            [new PratoProduzidoInput(ravioli.Id, 2, 500, 1144, 5)]));

        ravioli.ProdutoId.Should().NotBeNull("o que a casa produz é vinculado");
        _produtos[ravioli.ProdutoId!.Value].Nome.Should().Be("Ravióli de queijo");
        _categoriasGravadas.Should().ContainSingle(c => c.Nome == ProduzirPratosUseCase.CategoriaPratos);
        r.Pratos.Single().Should().Match<PratoProduzidoResult>(p => p.Porcoes == 2 && p.SobraG == 144 && p.CardapioItemId == ravioli.Id);
        r.TotalEtiquetas.Should().Be(2);
    }

    [Fact]
    public async Task SegundaProducao_ReusaOProdutoEACategoria()
    {
        var ravioli = Avulso("Ravióli");
        await Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null, [new PratoProduzidoInput(ravioli.Id, 2, 500, null, 5)]));
        var produtoDaPrimeira = ravioli.ProdutoId;

        await Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null, [new PratoProduzidoInput(ravioli.Id, 3, 500, null, 5)]));

        ravioli.ProdutoId.Should().Be(produtoDaPrimeira);
        _produtos.Should().HaveCount(1);
        _categoriasGravadas.Should().ContainSingle();
    }

    [Fact]
    public async Task PratoArquivado_NaoProduz()
    {
        var torta = Avulso("Torta");
        torta.Arquivar(DateTime.UtcNow);

        var act = () => Sut().ExecuteAsync(new ProduzirPratosCommand(EmpresaId, Guid.NewGuid(), null, [new PratoProduzidoInput(torta.Id, 1, null, null, 3)]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*fora do cardápio*");
    }
}
