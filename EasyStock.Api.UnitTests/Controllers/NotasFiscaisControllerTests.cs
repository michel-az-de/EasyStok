using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Fiscal.CancelarNfe;
using EasyStock.Application.UseCases.Fiscal.ConsultarNfe;
using EasyStock.Application.UseCases.Fiscal.EmitirNfce;
using EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Fiscal;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class NotasFiscaisControllerTests : IDisposable
{
    private readonly INfeRepository _nfeRepo = Substitute.For<INfeRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly EasyStockDbContext _db;
    private readonly NotasFiscaisController _controller;

    private static readonly Guid _empresaId = Guid.NewGuid();

    public NotasFiscaisControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.UsuarioId.Returns(Guid.Empty);

        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"nfe-tests-{Guid.NewGuid()}")
            .Options);

        var emitir = new EmitirNfceUseCase(
            _nfeRepo,
            Substitute.For<INumeracaoNfeService>(),
            Substitute.For<IGeradorChaveAcesso>(),
            Substitute.For<IGatewayFiscalFactory>(),
            Substitute.For<IConfigFiscalResolver>(),
            Substitute.For<IUnitOfWork>(),
            NullLogger<EmitirNfceUseCase>.Instance);

        var cancelar = new CancelarNfeUseCase(
            _nfeRepo,
            Substitute.For<IGatewayFiscalFactory>(),
            Substitute.For<IConfigFiscalResolver>(),
            Substitute.For<IUnitOfWork>(),
            NullLogger<CancelarNfeUseCase>.Instance);

        var inutilizar = new InutilizarNumeracaoUseCase(
            Substitute.For<IGatewayFiscalFactory>(),
            Substitute.For<IConfigFiscalResolver>(),
            NullLogger<InutilizarNumeracaoUseCase>.Instance);

        var consultar = new ConsultarNfeUseCase(_nfeRepo);

        _controller = new NotasFiscaisController(
            emitir, cancelar, inutilizar, consultar,
            _nfeRepo, _currentUser, _db);
    }

    public void Dispose() => _db.Dispose();

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
        _nfeRepo.GetByEmpresaAsync(_empresaId, 1, 20, null, null, null, null, Arg.Any<CancellationToken>())
            .Returns(((IEnumerable<NfeDocumento>)[], 0));

        var result = await _controller.Listar(_empresaId, null, null, null, null);

        var (items, meta) = OkPaged<EasyStock.Api.Models.Fiscal.NfeListItemResponse>(result);
        meta.Total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Listar_DeveChamarRepositorioComPaginacaoCorreta()
    {
        _nfeRepo.GetByEmpresaAsync(_empresaId, 2, 10, null, null, null, null, Arg.Any<CancellationToken>())
            .Returns(((IEnumerable<NfeDocumento>)[], 0));

        await _controller.Listar(_empresaId, null, null, null, null, page: 2, pageSize: 10);

        await _nfeRepo.Received(1).GetByEmpresaAsync(
            _empresaId, 2, 10, null, null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listar_DeveRetornarBadRequest_QuandoEmpresaIdVazioEhSuperAdmin()
    {
        var result = await _controller.Listar(Guid.Empty, null, null, null, null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Detalhe ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Detalhe_DeveRetornarNotFound_QuandoNfeNaoExiste()
    {
        var id = Guid.NewGuid();
        _nfeRepo.GetByIdWithDetailsAsync(_empresaId, id)
            .Returns((NfeDocumento?)null);

        var result = await _controller.Detalhe(id, _empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
