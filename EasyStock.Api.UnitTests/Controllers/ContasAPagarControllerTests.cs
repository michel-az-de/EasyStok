using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class ContasAPagarControllerTests
{
    private readonly IContaPagarRepository _contaRepo = Substitute.For<IContaPagarRepository>();
    private readonly ICategoriaFinanceiraRepository _categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
    private readonly ICentroCustoRepository _centroRepo = Substitute.For<ICentroCustoRepository>();
    private readonly ICaixaRepository _caixaRepo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ContasAPagarController _controller;

    private static readonly Guid _empresaId = Guid.NewGuid();

    public ContasAPagarControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.UsuarioId.Returns(Guid.Empty);
        _currentUser.TemPermissao(Permissao.VisualizarContasAPagar).Returns(true);
        _currentUser.TemPermissao(Permissao.GerenciarContasAPagar).Returns(true);

        _controller = new ContasAPagarController(
            new CriarContaPagarUseCase(_contaRepo, _categoriaRepo, _centroRepo, _uow, NullLogger<CriarContaPagarUseCase>.Instance),
            new AtualizarContaPagarUseCase(_contaRepo, _categoriaRepo, _centroRepo, _uow),
            new EmitirContaPagarUseCase(_contaRepo, _uow),
            new CancelarContaPagarUseCase(_contaRepo, _uow),
            new AdicionarParcelaContaPagarUseCase(_contaRepo, _uow),
            new RemoverParcelaContaPagarUseCase(_contaRepo, _uow),
            new ListarContasPagarUseCase(_contaRepo),
            new ObterContaPagarDetalheUseCase(_contaRepo),
            new RegistrarPagamentoParcelaPagarUseCase(_contaRepo, _caixaRepo, _uow, NullLogger<RegistrarPagamentoParcelaPagarUseCase>.Instance),
            new EstornarPagamentoParcelaPagarUseCase(_contaRepo, _caixaRepo, _uow, NullLogger<EstornarPagamentoParcelaPagarUseCase>.Instance),
            _currentUser);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (IEnumerable<T> Items, PagedMeta Meta) OkPaged<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<T>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        return (envelope.Data, meta);
    }

    // ── Listar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_DeveRetornarOkPaginado()
    {
        _contaRepo.ListarAsync(
            _empresaId, null, null, null, null, null, null, null,
            1, 20, "datavencimento", "asc", Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EasyStock.Domain.Entities.Financeiro.ContaPagar>)[], 0));

        var result = await _controller.Listar(_empresaId, null, null, null, null, null, null, null);

        var (items, meta) = OkPaged<EasyStock.Application.UseCases.Financeiro.Common.ContaPagarResult>(result);
        meta.Total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Listar_DeveChamarRepositorioComPaginacaoCorreta()
    {
        _contaRepo.ListarAsync(
            _empresaId, null, null, null, null, null, null, null,
            2, 10, "datavencimento", "asc", Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EasyStock.Domain.Entities.Financeiro.ContaPagar>)[], 0));

        await _controller.Listar(_empresaId, null, null, null, null, null, null, null, page: 2, pageSize: 10);

        await _contaRepo.Received(1).ListarAsync(
            _empresaId, null, null, null, null, null, null, null,
            2, 10, "datavencimento", "asc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listar_DeveRetornarForbid_QuandoSemPermissao()
    {
        _currentUser.TemPermissao(Permissao.VisualizarContasAPagar).Returns(false);

        var result = await _controller.Listar(_empresaId, null, null, null, null, null, null, null);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Listar_DeveRetornarBadRequest_QuandoEmpresaIdVazioEhSuperAdmin()
    {
        var result = await _controller.Listar(Guid.Empty, null, null, null, null, null, null, null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Detalhe ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Detalhe_DeveRetornarNotFound_QuandoContaNaoExiste()
    {
        var id = Guid.NewGuid();
        _contaRepo.GetByIdWithDetailsAsync(_empresaId, id, Arg.Any<CancellationToken>())
            .Returns((EasyStock.Domain.Entities.Financeiro.ContaPagar?)null);

        var result = await _controller.Detalhe(id, _empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Detalhe_DeveRetornarForbid_QuandoSemPermissao()
    {
        _currentUser.TemPermissao(Permissao.VisualizarContasAPagar).Returns(false);

        var result = await _controller.Detalhe(Guid.NewGuid(), _empresaId);

        result.Should().BeOfType<ForbidResult>();
    }

    // ── Atualizar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Atualizar_DeveRetornarBadRequest_QuandoIdsDivergirem()
    {
        var routeId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        var cmd = new AtualizarContaPagarCommand(_empresaId, bodyId);

        var result = await _controller.Atualizar(routeId, cmd);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
