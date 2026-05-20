using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarFornecedor;
using EasyStock.Application.UseCases.CriarFornecedor;
using EasyStock.Application.UseCases.DesativarFornecedor;
using EasyStock.Application.UseCases.ReativarFornecedor;
using EasyStock.Application.UseCases.Fornecedor;
using EasyStock.Application.UseCases.ListarFornecedores;
using EasyStock.Application.UseCases.Pedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class FornecedorControllerTests
{
    private readonly IFornecedorRepository _fornecedorRepository = Substitute.For<IFornecedorRepository>();
    private readonly IPedidoFornecedorRepository _pedidoFornecedorRepository = Substitute.For<IPedidoFornecedorRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private readonly FornecedorController _controller;

    public FornecedorControllerTests()
    {
        var criar = new CriarFornecedorUseCase(_fornecedorRepository, _assinaturaRepository, _unitOfWork, Substitute.For<ILogger<CriarFornecedorUseCase>>());
        var atualizar = new AtualizarFornecedorUseCase(_fornecedorRepository, _unitOfWork, Substitute.For<ILogger<AtualizarFornecedorUseCase>>());
        var desativar = new DesativarFornecedorUseCase(_fornecedorRepository, _pedidoFornecedorRepository, _unitOfWork, Substitute.For<ILogger<DesativarFornecedorUseCase>>());
        var reativar = new ReativarFornecedorUseCase(_fornecedorRepository, _unitOfWork, Substitute.For<ILogger<ReativarFornecedorUseCase>>());
        var listar = new ListarFornecedoresUseCase(_fornecedorRepository);
        var detalhe = new ObterFornecedorDetalheUseCase(_fornecedorRepository);
        var historico = new ObterHistoricoFornecedorUseCase(_fornecedorRepository, _pedidoFornecedorRepository);
        var estatisticas = new ObterEstatisticasFornecedorUseCase(_fornecedorRepository, _pedidoFornecedorRepository);
        var pedidosAbertos = new ListarPedidosAbertosUseCase(_pedidoFornecedorRepository);
        var criarPedido = new CriarPedidoFornecedorUseCase(_pedidoFornecedorRepository, _fornecedorRepository, _unitOfWork, Substitute.For<ILogger<CriarPedidoFornecedorUseCase>>());
        var receberPedido = new ReceberPedidoFornecedorUseCase(_pedidoFornecedorRepository, null!, _unitOfWork, Substitute.For<ILogger<ReceberPedidoFornecedorUseCase>>());
        var cancelarPedido = new CancelarPedidoFornecedorUseCase(_pedidoFornecedorRepository, _unitOfWork, Substitute.For<ILogger<CancelarPedidoFornecedorUseCase>>());
        var entradaUseCase = new RegistrarEntradaEstoqueUseCase(
            Substitute.For<IProdutoRepository>(),
            Substitute.For<IProdutoVariacaoRepository>(),
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            _unitOfWork,
            Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>());
        var processarRecebimento = new ProcessarRecebimentoPedidoFornecedorUseCase(
            _pedidoFornecedorRepository,
            Substitute.For<IPedidoFornecedorItemRepository>(),
            entradaUseCase,
            _unitOfWork,
            Substitute.For<ILogger<ProcessarRecebimentoPedidoFornecedorUseCase>>());
        var alteracoes = new EasyStock.Application.UseCases.ObterHistoricoAlteracoesFornecedor.ObterHistoricoAlteracoesFornecedorUseCase(_fornecedorRepository);
        var receberCompleto = new ReceberPedidoCompletoUseCase(Substitute.For<IPedidoFornecedorItemRepository>(), processarRecebimento);

        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _controller = new FornecedorController(criar, atualizar, desativar, reativar, listar, detalhe, historico, estatisticas, pedidosAbertos, criarPedido, receberPedido, cancelarPedido, processarRecebimento, receberCompleto, alteracoes, _currentUser);
    }

    [Fact]
    public async Task Update_DeveRetornarBadRequest_QuandoIdDaRotaDivergirDoBody()
    {
        var result = await _controller.Update(Guid.NewGuid(), new AtualizarFornecedorCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Fornecedor",
            null, null, null, null, null, null, null, null, null, null, null));

        // Novo contrato: BadRequestObjectResult com { error: { code: "BAD_REQUEST" } }
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result;
        var envelope = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        envelope.Error.Code.Should().Be("BAD_REQUEST");
    }

    [Fact]
    public async Task GetEstatisticas_DeveRetornarEnvelopeComEstatisticas()
    {
        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        _fornecedorRepository.GetByIdAsync(empresaId, fornecedorId).Returns(new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor Teste"
        });
        _pedidoFornecedorRepository.GetEstatisticasAsync(empresaId, fornecedorId).Returns((2, 1000m, 8m, 1m));

        var result = await _controller.GetEstatisticas(fornecedorId, empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<FornecedorEstatisticasResult>>().Subject;
        envelope.Data.TotalGasto.Should().Be(1000m);
    }

    [Fact]
    public async Task GetAll_DeveRetornarPagedEnvelope()
    {
        var empresaId = Guid.NewGuid();
        _fornecedorRepository.GetByEmpresaAsync(empresaId, 1, 20, null, null, "criadoem", "desc")
            .Returns((new List<Fornecedor>
            {
                new() { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Fornecedor A", Ativo = true }
            }, 1));

        var result = await _controller.GetAll(empresaId, 1, 20);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<FornecedorResult>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        meta.Total.Should().Be(1);
        meta.Limit.Should().Be(20);
    }
}
