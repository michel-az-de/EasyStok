using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.EstornarSaida;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class ItemEstoqueControllerTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IProdutoRepository _produtoRepository = Substitute.For<IProdutoRepository>();
    private readonly IProdutoVariacaoRepository _produtoVariacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
    private readonly IFornecedorRepository _fornecedorRepository = Substitute.For<IFornecedorRepository>();
    private readonly IMovimentacaoEstoqueRepository _movimentacaoEstoqueRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IVendaRepository _vendaRepository = Substitute.For<IVendaRepository>();
    private readonly IItemVendaRepository _itemVendaRepository = Substitute.For<IItemVendaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILogger<RegistrarEntradaEstoqueUseCase> _registrarEntradaLogger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
    private readonly ILogger<RegistrarSaidaEstoqueUseCase> _registrarSaidaLogger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
    private readonly ILogger<EstornarSaidaUseCase> _estornarSaidaLogger = Substitute.For<ILogger<EstornarSaidaUseCase>>();
    private readonly RegistrarEntradaEstoqueUseCase _registrarEntradaUseCase;
    private readonly RegistrarSaidaEstoqueUseCase _registrarSaidaUseCase;
    private readonly EstornarSaidaUseCase _estornarSaidaUseCase;
    private readonly ReporEstoqueUseCase _reporEstoqueUseCase;
    private readonly BuscarEstoqueInteligenteUseCase _buscarUseCase;
    private readonly ItemEstoqueController _controller;

    public ItemEstoqueControllerTests()
    {
        _registrarEntradaUseCase = new RegistrarEntradaEstoqueUseCase(
            _produtoRepository,
            _produtoVariacaoRepository,
            _itemEstoqueRepository,
            _movimentacaoEstoqueRepository,
            _unitOfWork,
            _registrarEntradaLogger);
        _registrarSaidaUseCase = new RegistrarSaidaEstoqueUseCase(
            _produtoRepository,
            _itemEstoqueRepository,
            _vendaRepository,
            _itemVendaRepository,
            _movimentacaoEstoqueRepository,
            _unitOfWork,
            _registrarSaidaLogger);
        _estornarSaidaUseCase = new EstornarSaidaUseCase(
            _movimentacaoEstoqueRepository,
            _itemEstoqueRepository,
            _unitOfWork,
            _estornarSaidaLogger);
        _reporEstoqueUseCase = new ReporEstoqueUseCase(
            _produtoRepository,
            _itemEstoqueRepository,
            _movimentacaoEstoqueRepository,
            _unitOfWork);
        _buscarUseCase = new BuscarEstoqueInteligenteUseCase(
            _produtoRepository,
            _produtoVariacaoRepository,
            _itemEstoqueRepository,
            _fornecedorRepository,
            Substitute.For<IPedidoFornecedorRepository>(),
            Substitute.For<ILojaRepository>(),
            Substitute.For<IUsuarioRepository>(),
            _movimentacaoEstoqueRepository);
        _controller = new ItemEstoqueController(
            _itemEstoqueRepository,
            _registrarEntradaUseCase,
            _registrarSaidaUseCase,
            _estornarSaidaUseCase,
            _reporEstoqueUseCase,
            _buscarUseCase,
            _currentUser);
    }

    [Fact]
    public async Task GetAll_DeveRetornarOk_ComListaDeItens()
    {
        var empresaId = Guid.NewGuid();
        var itens = new List<ItemEstoque>
        {
            new() { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(10) }
        };
        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20).Returns((itens, 1));

        var result = await _controller.GetAll(empresaId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
        var metaProp = ok.Value!.GetType().GetProperty("Meta");
        metaProp.Should().NotBeNull("o envelope deve ter propriedade Meta");
        var meta = metaProp!.GetValue(ok.Value).Should().BeOfType<PagedMeta>().Subject;
        meta.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetById_DeveRetornarOk_ComDtoNoFormatoEstoqueSku()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            Status = EasyStock.Domain.Enums.StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow.AddDays(-5),
            Produto = new Produto
            {
                Id = produtoId,
                EmpresaId = empresaId,
                Nome = "Produto Teste",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CategoriaId = Guid.NewGuid()
            }
        };
        _itemEstoqueRepository.GetItemComProdutoAsync(empresaId, item.Id).Returns(item);

        var result = await _controller.GetById(item.Id, empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value!;
        // Response is ApiResponse<T> — access .Data to get the DTO
        var dataProp = envelope.GetType().GetProperty("Data");
        dataProp.Should().NotBeNull();
        var dto = dataProp!.GetValue(envelope)!;

        // GetById now returns the same DTO shape as GetAll (EstoqueSku-compatible)
        var idProp = dto.GetType().GetProperty("id");
        idProp.Should().NotBeNull("o DTO deve ter campo 'id'");
        idProp!.GetValue(dto).Should().Be(item.Id.ToString());

        var qtyProp = dto.GetType().GetProperty("qty");
        qtyProp.Should().NotBeNull("o DTO deve ter campo 'qty'");
        qtyProp!.GetValue(dto).Should().Be(10);

        var statusProp = dto.GetType().GetProperty("status");
        statusProp.Should().NotBeNull("o DTO deve ter campo 'status'");
        statusProp!.GetValue(dto).Should().Be("ok");
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoItemNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _itemEstoqueRepository.GetItemComProdutoAsync(empresaId, id).Returns((ItemEstoque?)null);

        var result = await _controller.GetById(id, empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RegistrarEntrada_DeveRetornarCreated_ComResultado()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var variacaoId = Guid.NewGuid();
        var command = new RegistrarEntradaEstoqueCommand(
            empresaId,
            produtoId,
            variacaoId,
            10,
            199.90m,
            249.90m,
            DateTime.UtcNow,
            NaturezaMovimentacaoEstoque.Compra,
            "CAP3426",
            "LOTE-01",
            "ML-123",
            "Preto / P",
            "Preto",
            "P",
            "Fornecedor Teste",
            DateTime.UtcNow.AddMonths(6),
            "Entrada teste",
            null,
            "DOC-01",
            new DimensoesInput(0.35m, 10m, 5m, 12m),
            "Anuncio para marketplace");
        _produtoRepository.GetByIdAsync(produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CategoriaId = Guid.NewGuid()
        });
        _produtoVariacaoRepository.GetByIdAsync(variacaoId).Returns(new ProdutoVariacao
        {
            Id = variacaoId,
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            Nome = "Preto / P",
            DescricaoComercial = "Buds FE Preto",
            Ativa = true
        });
        _unitOfWork.CommitAsync().Returns(1);

        var result = await _controller.RegistrarEntrada(command);

        result.Should().BeOfType<CreatedResult>();
        var createdResult = (CreatedResult)result;
        var envelope = createdResult.Value.Should().BeOfType<ApiResponse<RegistrarEntradaEstoqueResult>>().Subject;
        envelope.Data.ItemEstoqueId.Should().NotBe(Guid.Empty);
        envelope.Data.MovimentacaoId.Should().NotBe(Guid.Empty);
        envelope.Data.ChavePesquisa.Should().Contain("CAP3426");
    }

    [Fact]
    public async Task RegistrarSaida_DeveRetornarOk_ComResultado()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var itemCommand = new RegistrarSaidaEstoqueItemCommand(itemId, 5, 399.90m, "Venda teste");
        var command = new RegistrarSaidaEstoqueCommand(
            empresaId,
            new[] { itemCommand },
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            "NF-123",
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "Saida teste");
        _itemEstoqueRepository.GetByIdComLockAsync(empresaId, itemId).Returns(new ItemEstoque
        {
            Id = itemId,
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(8),
            QuantidadeInicial = Quantidade.From(8),
            CustoUnitario = Dinheiro.FromDecimal(150m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow.AddDays(-2),
            UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-1)
        });
        _produtoRepository.GetByIdAsync(produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            DescricaoBase = "Fone bluetooth",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CategoriaId = Guid.NewGuid()
        });
        _unitOfWork.CommitAsync().Returns(1);

        var result = await _controller.RegistrarSaida(command);

        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<RegistrarSaidaEstoqueResult>>().Subject;
        envelope.Data.VendaId.Should().NotBe(Guid.Empty);
        envelope.Data.Itens.Should().ContainSingle();
        envelope.Data.ValorTotal.Should().Be(1999.50m);
    }

    [Fact]
    public async Task RegistrarSaida_DeveAceitarSaidaPorProdutoEConsumirLotesEmFifo()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var loteAntigoId = Guid.NewGuid();
        var loteNovoId = Guid.NewGuid();
        var itemCommand = new RegistrarSaidaEstoqueItemCommand(produtoId, null, 12, 399.90m, "Venda FIFO");
        var command = new RegistrarSaidaEstoqueCommand(
            empresaId,
            new[] { itemCommand },
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            "NF-456",
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "Saida fifo teste");

        _itemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null).Returns(
            new[]
            {
                new ItemEstoque
                {
                    Id = loteAntigoId,
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeAtual = Quantidade.From(10),
                    QuantidadeInicial = Quantidade.From(10),
                    QuantidadeMinima = 5,
                    CustoUnitario = Dinheiro.FromDecimal(150m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow.AddDays(-5),
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-3)
                },
                new ItemEstoque
                {
                    Id = loteNovoId,
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeAtual = Quantidade.From(5),
                    QuantidadeInicial = Quantidade.From(5),
                    QuantidadeMinima = 5,
                    CustoUnitario = Dinheiro.FromDecimal(150m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow.AddDays(-1),
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-1)
                }
            });
        _produtoRepository.GetByIdAsync(produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            DescricaoBase = "Fone bluetooth",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CategoriaId = Guid.NewGuid()
        });
        _movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(0.2m);
        _unitOfWork.CommitAsync().Returns(1);

        var result = await _controller.RegistrarSaida(command);

        result.Should().BeOfType<OkObjectResult>();
        var envelope2 = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<RegistrarSaidaEstoqueResult>>().Subject;
        envelope2.Data.Itens.Should().HaveCount(2);
        envelope2.Data.Itens.Select(i => i.ItemEstoqueId).Should().Equal(loteAntigoId, loteNovoId);
        envelope2.Data.Itens.Select(i => i.QuantidadeSaida).Should().Equal(10, 2);
        envelope2.Data.ValorTotal.Should().Be(4798.80m);
    }

    [Fact]
    public async Task ReporEstoque_DeveRetornarOk_ComResultado()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var command = new ReporEstoqueCommand(
            empresaId,
            itemId,
            20,
            180m,
            239.90m,
            DateTime.UtcNow,
            "Preto / M",
            "Preto",
            "M",
            "Reposicao teste",
            "DOC-REP-01",
            new DimensoesInput(0.4m, 10m, 5m, 12m),
            DateTime.UtcNow.AddMonths(8));
        _itemEstoqueRepository.GetByIdAsync(itemId).Returns(new ItemEstoque
        {
            Id = itemId,
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(150m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow.AddDays(-10),
            UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-2)
        });
        _produtoRepository.GetByIdAsync(produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CategoriaId = Guid.NewGuid()
        });
        _unitOfWork.CommitAsync().Returns(1);

        var result = await _controller.ReporEstoque(command);

        result.Should().BeOfType<OkObjectResult>();
        var envelope3 = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<ReporEstoqueResult>>().Subject;
        envelope3.Data.ItemEstoqueId.Should().Be(itemId);
        envelope3.Data.QuantidadeAnterior.Should().Be(10);
        envelope3.Data.QuantidadeAtual.Should().Be(30);
    }

}
