using System.Security.Claims;
using EasyStock.Api.Controllers;
using EasyStock.Api.UnitTests.Helpers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.FinalizarLote;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#1776 (item 9): o lote da produção pelo console gravava OperadorNome vazio; o controller passava null.</summary>
public class AtendimentoProducaoControllerOperadorTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly ICategoriaRepository _categorias = Substitute.For<ICategoriaRepository>();
    private readonly ILoteRepository _loteRepo = Substitute.For<ILoteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IProdutoVariacaoRepository _variacoes = Substitute.For<IProdutoVariacaoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<Categoria> _categoriasGravadas = [];
    private readonly Dictionary<Guid, Produto> _produtos = [];
    private Lote? _lote;

    public AtendimentoProducaoControllerOperadorTests()
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
    }

    [Fact]
    public async Task Produzir_GravaONomeDaSessaoComoOperadorDoLote()
    {
        var prato = CardapioItem.CriarAvulso(_vitrine.Id, "Lasanha", 45m);
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, prato.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(prato);
        var controller = Controller(new Claim("nome", "Ana Souza"));

        var resposta = await controller.Produzir(
            new ProduzirPratosRequest([new PratoProduzidoInput(prato.Id, 2, 300, null, 3)], null), CancellationToken.None);

        resposta.Should().BeOfType<CreatedResult>();
        _lote!.OperadorNome.Should().Be("Ana Souza");
    }

    private AtendimentoProducaoController Controller(params Claim[] claims)
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.EmpresaId.Returns(EmpresaId);
        currentUser.UsuarioId.Returns(Guid.NewGuid());
        currentUser.TemPermissao(Permissao.GerenciarEstoque).Returns(true);

        var registrar = new RegistrarProducaoUseCase(
            new CriarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<CriarLoteUseCase>>()),
            new FinalizarLoteUseCase(_loteRepo, _produtoRepo, _uow, Substitute.For<ILogger<FinalizarLoteUseCase>>()),
            new RegistrarEntradaEstoqueUseCase(_produtoRepo, _variacoes, Substitute.For<IItemEstoqueRepository>(),
                Substitute.For<IMovimentacaoEstoqueRepository>(), _uow, Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(),
                publicadorEventos: Substitute.For<IPublicadorEventos>(), loteRepository: _loteRepo),
            _produtoRepo, _uow, Substitute.For<ILogger<RegistrarProducaoUseCase>>());
        var ligar = new LigarPratoAoEstoqueUseCase(_storefronts, _cardapio, _categorias, _produtoRepo,
            new CadastrarProdutoUseCase(_produtoRepo, _categorias,
                Substitute.For<IProdutoCaracteristicaRepository>(), Substitute.For<IProdutoEmbalagemRepository>(), _variacoes,
                _uow, Substitute.For<ILogger<CadastrarProdutoUseCase>>()),
            _uow);
        var produzir = new ProduzirPratosUseCase(_storefronts, _cardapio, ligar, registrar, _uow,
            new VincularPorcoesAoEstoqueUseCase(_variacoes));

        return new AtendimentoProducaoController(null!, produzir, null!, null!, null!, null!, currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
    }
}
