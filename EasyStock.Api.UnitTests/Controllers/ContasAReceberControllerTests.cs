using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Financeiro;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class ContasAReceberControllerTests
{
    private readonly IContaReceberRepository _contaRepo = Substitute.For<IContaReceberRepository>();
    private readonly ICategoriaFinanceiraRepository _categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
    private readonly ICentroCustoRepository _centroRepo = Substitute.For<ICentroCustoRepository>();
    private readonly ICaixaRepository _caixaRepo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ContasAReceberController _controller;

    private static readonly Guid _empresaId = Guid.NewGuid();

    public ContasAReceberControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.UsuarioId.Returns(Guid.Empty);
        _currentUser.TemPermissao(Permissao.VisualizarContasAReceber).Returns(true);
        _currentUser.TemPermissao(Permissao.GerenciarContasAReceber).Returns(true);

        var criarContaReceber = new CriarContaReceberUseCase(
            _contaRepo, _categoriaRepo, _centroRepo, _uow,
            NullLogger<CriarContaReceberUseCase>.Instance);

        var efiPix = Substitute.For<EasyStock.Application.Ports.Output.IEfiPixService>();

        _controller = new ContasAReceberController(
            criarContaReceber,
            new AtualizarContaReceberUseCase(_contaRepo, _categoriaRepo, _centroRepo, _uow),
            new EmitirContaReceberUseCase(_contaRepo, _uow),
            new CancelarContaReceberUseCase(_contaRepo, _uow),
            new AdicionarParcelaContaReceberUseCase(_contaRepo, _uow),
            new RemoverParcelaContaReceberUseCase(_contaRepo, _uow),
            new ListarContasReceberUseCase(_contaRepo),
            new ObterContaReceberDetalheUseCase(_contaRepo),
            new RegistrarPagamentoParcelaReceberUseCase(_contaRepo, _caixaRepo, _uow, NullLogger<RegistrarPagamentoParcelaReceberUseCase>.Instance),
            new EstornarPagamentoParcelaReceberUseCase(_contaRepo, _caixaRepo, _uow, NullLogger<EstornarPagamentoParcelaReceberUseCase>.Instance),
            new GerarPixQrParcelaReceberUseCase(_contaRepo, efiPix, _uow, NullLogger<GerarPixQrParcelaReceberUseCase>.Instance),
            new LimparPixParcelaReceberUseCase(_contaRepo, _uow),
            new ReconciliarPixParcelaReceberUseCase(_contaRepo, _caixaRepo, efiPix, _uow, NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance),
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
            .Returns(((IReadOnlyList<EasyStock.Domain.Entities.Financeiro.ContaReceber>)[], 0));

        var result = await _controller.Listar(_empresaId, null, null, null, null, null, null, null);

        var (items, meta) = OkPaged<EasyStock.Application.UseCases.Financeiro.Common.ContaReceberResult>(result);
        meta.Total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Listar_DeveChamarRepositorioComPaginacaoCorreta()
    {
        _contaRepo.ListarAsync(
            _empresaId, null, null, null, null, null, null, null,
            3, 5, "datavencimento", "asc", Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EasyStock.Domain.Entities.Financeiro.ContaReceber>)[], 0));

        await _controller.Listar(_empresaId, null, null, null, null, null, null, null, page: 3, pageSize: 5);

        await _contaRepo.Received(1).ListarAsync(
            _empresaId, null, null, null, null, null, null, null,
            3, 5, "datavencimento", "asc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listar_DeveRetornarForbid_QuandoSemPermissao()
    {
        _currentUser.TemPermissao(Permissao.VisualizarContasAReceber).Returns(false);

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
            .Returns((EasyStock.Domain.Entities.Financeiro.ContaReceber?)null);

        var result = await _controller.Detalhe(id, _empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Detalhe_DeveRetornarForbid_QuandoSemPermissao()
    {
        _currentUser.TemPermissao(Permissao.VisualizarContasAReceber).Returns(false);

        var result = await _controller.Detalhe(Guid.NewGuid(), _empresaId);

        result.Should().BeOfType<ForbidResult>();
    }

    // ── Atualizar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Atualizar_DeveRetornarBadRequest_QuandoIdsDivergirem()
    {
        var routeId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        var cmd = new AtualizarContaReceberCommand(_empresaId, bodyId);

        var result = await _controller.Atualizar(routeId, cmd);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
