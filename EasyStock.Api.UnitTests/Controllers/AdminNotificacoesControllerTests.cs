using System.Reflection;
using System.Text.Json;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// N13: o endpoint de disparo de teste é de superadmin (policy <c>SuperAdmin</c>, que já existe), exige motivo auditado,
/// recusa tipo fora do catálogo com a lista dos válidos e nunca lê o destinatário do corpo.
/// </summary>
public class AdminNotificacoesControllerTests : IDisposable
{
    private const string Motivo = "Smoke de producao do catalogo";

    private readonly EasyStockDbContext _db;
    private readonly ICurrentUserAccessor _usuarioAtual = Substitute.For<ICurrentUserAccessor>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly Guid _superadminId = Guid.NewGuid();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly AdminNotificacoesController _controller;

    public AdminNotificacoesControllerTests()
    {
        var sistema = Substitute.For<ICurrentUserAccessor>();
        sistema.IsAuthenticated.Returns(true);
        sistema.Nivel.Returns(NivelAcesso.SuperAdmin);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"admin-notif-{Guid.NewGuid()}").Options, sistema);

        _usuarioAtual.UsuarioId.Returns(_superadminId);
        _usuarioAtual.EmpresaId.Returns(_empresaId);
        var superadmin = Usuario.Criar("Felipe", "felipe@example.com", "hash");
        superadmin.EmailConfirmado = true;
        var usuarios = Substitute.For<IUsuarioRepository>();
        usuarios.GetByIdAsync(_superadminId).Returns(superadmin);
        _notificador.EnfileirarEventoAsync(Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var useCase = new DispararTesteNotificacaoUseCase(
            _usuarioAtual, usuarios, Substitute.For<IEmpresaPadraoResolver>(), _notificador,
            Substitute.For<IEventoNotificacaoRepository>(), Substitute.For<IOutboxNotificacaoRepository>(),
            Substitute.For<ITenantContextAccessor>(), Substitute.For<IUnitOfWork>(),
            NullLogger<DispararTesteNotificacaoUseCase>.Instance);

        var auditoria = new AdminAuditService(_db, new HttpContextAccessor(), NullLogger<AdminAuditService>.Instance);
        _controller = new AdminNotificacoesController(useCase, new LimitadorDisparoTeste(TimeProvider.System), _usuarioAtual, auditoria)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void AdminComumRecebe403()
    {
        // A policy SuperAdmin exige o nível SuperAdmin: o Admin de tenant não passa por ela (mesma trava do diagnóstico).
        typeof(AdminNotificacoesController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("SuperAdmin");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("curto")]
    public async Task SemMotivoRecebe400(string? motivo)
    {
        var resultado = await _controller.DispararTeste(new DispararTesteRequest("IncidenteSistema", motivo), CancellationToken.None);

        resultado.Should().BeOfType<BadRequestObjectResult>();
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Theory]
    [InlineData("PedidoEntregue")]
    [InlineData("NaoExiste")]
    [InlineData(null)]
    public async Task TipoDesconhecidoRecebe400ComALista(string? tipo)
    {
        var resultado = await _controller.DispararTeste(new DispararTesteRequest(tipo, Motivo), CancellationToken.None);

        var erro = resultado.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ApiErrorResponse>().Subject.Error;
        var validos = (IEnumerable<string>)erro.Details!;
        validos.Should().BeEquivalentTo("ResetSenha", "ConviteAcesso", "IncidenteSistema", "PrazoEstourado", "ResumoDiario", "ContatoAlterado", "SenhaAlterada");
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task DestinatarioNuncaVemDoCorpo()
    {
        var corpo = """{"tipo":"IncidenteSistema","motivo":"Smoke de producao do catalogo","destinatario":"intruso@example.com","email":"intruso@example.com"}""";
        var requisicao = JsonSerializer.Deserialize<DispararTesteRequest>(corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var resultado = await _controller.DispararTeste(requisicao, CancellationToken.None);

        resultado.Should().BeOfType<AcceptedResult>();
        var payload = (string)_notificador.ReceivedCalls().Single().GetArguments()[2]!;
        payload.Should().Contain("felipe@example.com").And.NotContain("intruso@example.com");
    }

    [Fact]
    public async Task DisparoValidoResponde202ComOEventoEAuditaSemEndereco()
    {
        var resultado = await _controller.DispararTeste(new DispararTesteRequest("ResetSenha", Motivo), CancellationToken.None);

        resultado.Should().BeOfType<AcceptedResult>();
        var auditoria = await _db.AdminAuditLogs.IgnoreQueryFilters().SingleAsync();
        auditoria.Motivo.Should().Be(Motivo);
        $"{auditoria.Acao} {auditoria.Detalhes}".Should().NotContain("felipe@example.com");
    }

    [Fact]
    public async Task DecimoPrimeiroDisparoNaHoraRecebe429()
    {
        for (var i = 0; i < 10; i++)
            (await _controller.DispararTeste(new DispararTesteRequest("ResumoDiario", Motivo), CancellationToken.None))
                .Should().BeOfType<AcceptedResult>();

        var resultado = await _controller.DispararTeste(new DispararTesteRequest("ResumoDiario", Motivo), CancellationToken.None);

        resultado.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        _controller.Response.Headers.Should().ContainKey("Retry-After");
    }
}
