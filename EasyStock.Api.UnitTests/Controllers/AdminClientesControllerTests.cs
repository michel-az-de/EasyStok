using EasyStock.Api.Controllers;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.AnonimizarUsuarioPorAdmin;
using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ReativarLoja;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AdminClientesControllerTests : IDisposable
{
    private readonly ILojaRepository _lojaRepo = Substitute.For<ILojaRepository>();
    private readonly EasyStockDbContext _db;
    private readonly AdminClientesController _controller;

    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _lojaId = Guid.NewGuid();
    private const string MotivoValido = "Motivo de teste com tamanho suficiente";
    private const string MotivoInvalido = "curto";

    public AdminClientesControllerTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"admin-clientes-tests-{Guid.NewGuid()}")
            .Options);

        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns((HttpContext?)null);

        var audit = new AdminAuditService(_db, http, NullLogger<AdminAuditService>.Instance);

        var uow = Substitute.For<IUnitOfWork>();

        var criarLoja = new CriarLojaUseCase(
            _lojaRepo,
            Substitute.For<IAssinaturaEmpresaRepository>(),
            uow,
            NullLogger<CriarLojaUseCase>.Instance);

        var atualizarLoja = new AtualizarLojaUseCase(
            _lojaRepo,
            uow,
            NullLogger<AtualizarLojaUseCase>.Instance);

        var desativarLoja = new DesativarLojaUseCase(
            _lojaRepo,
            uow,
            NullLogger<DesativarLojaUseCase>.Instance);

        var reativarLoja = new ReativarLojaUseCase(
            _lojaRepo,
            uow,
            NullLogger<ReativarLojaUseCase>.Instance);

        var anonimizar = new AnonimizarUsuarioPorAdminUseCase(
            Substitute.For<IUsuarioRepository>(),
            Substitute.For<IRefreshTokenRepository>(),
            Substitute.For<IResetTokenRepository>(),
            Substitute.For<IEmailConfirmationTokenRepository>(),
            uow,
            NullLogger<AnonimizarUsuarioPorAdminUseCase>.Instance);

        _controller = new AdminClientesController(
            _db,
            _lojaRepo,
            criarLoja,
            atualizarLoja,
            desativarLoja,
            reativarLoja,
            anonimizar,
            audit,
            http,
            NullLogger<AdminClientesController>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── CriarLoja ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CriarLoja_DeveRetornarBadRequest_QuandoTenantIdVazio()
    {
        var req = new CriarLojaAdminRequest(MotivoValido, "Loja Teste", null, null, null, null);

        var result = await _controller.CriarLoja(Guid.Empty, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CriarLoja_DeveRetornarBadRequest_QuandoMotivoMuitoCurto()
    {
        var req = new CriarLojaAdminRequest(MotivoInvalido, "Loja Teste", null, null, null, null);

        var result = await _controller.CriarLoja(_tenantId, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CriarLoja_DeveRetornarBadRequest_QuandoNomeVazio()
    {
        var req = new CriarLojaAdminRequest(MotivoValido, "", null, null, null, null);

        var result = await _controller.CriarLoja(_tenantId, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── AtualizarLoja ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AtualizarLoja_DeveRetornarBadRequest_QuandoTenantIdVazio()
    {
        var req = new AtualizarLojaAdminRequest(MotivoValido, "Loja Teste", null, null, null, null);

        var result = await _controller.AtualizarLoja(Guid.Empty, _lojaId, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AtualizarLoja_DeveRetornarBadRequest_QuandoLojaIdVazio()
    {
        var req = new AtualizarLojaAdminRequest(MotivoValido, "Loja Teste", null, null, null, null);

        var result = await _controller.AtualizarLoja(_tenantId, Guid.Empty, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AtualizarLoja_DeveRetornarBadRequest_QuandoMotivoMuitoCurto()
    {
        var req = new AtualizarLojaAdminRequest(MotivoInvalido, "Loja Teste", null, null, null, null);

        var result = await _controller.AtualizarLoja(_tenantId, _lojaId, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── ToggleLoja ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleLoja_DeveRetornarBadRequest_QuandoTenantIdVazio()
    {
        var req = new ToggleLojaRequest(MotivoValido, false);

        var result = await _controller.ToggleLoja(Guid.Empty, _lojaId, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ToggleLoja_DeveRetornarBadRequest_QuandoLojaIdVazio()
    {
        var req = new ToggleLojaRequest(MotivoValido, false);

        var result = await _controller.ToggleLoja(_tenantId, Guid.Empty, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ToggleLoja_DeveRetornarBadRequest_QuandoMotivoMuitoCurto()
    {
        var req = new ToggleLojaRequest(MotivoInvalido, false);

        var result = await _controller.ToggleLoja(_tenantId, _lojaId, req);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── GetAtividade ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAtividade_DeveRetornarBadRequest_QuandoTenantIdVazio()
    {
        var result = await _controller.GetAtividade(Guid.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
