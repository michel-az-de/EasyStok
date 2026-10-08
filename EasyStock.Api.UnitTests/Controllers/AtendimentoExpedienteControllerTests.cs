using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Expediente;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1443: horário e mensagens são da dona (Admin); abrir e fechar na mão é de gerente para cima, e fechar
/// dentro do horário pede justificativa. A regra mora no use case; o controller só leva nível e justificativa.
/// </summary>
public class AtendimentoExpedienteControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IExpedienteLojaRepository _repo = Substitute.For<IExpedienteLojaRepository>();
    private readonly IAuditLogRepository _auditoria = Substitute.For<IAuditLogRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    // Segunda, 28/09/2026, 12:00 em Brasília: dentro do turno padrão 08–22 h.
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero));

    private AtendimentoExpedienteController Controller(NivelAcesso nivel)
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(_usuarioId);
        _currentUser.Nivel.Returns(nivel);
        var uow = new FakeUnitOfWork();
        var publisher = Substitute.For<IOperacaoEventPublisher>();
        return new AtendimentoExpedienteController(
            new ObterExpedienteLojaUseCase(_repo, _relogio),
            new AtualizarExpedienteLojaUseCase(_repo, uow, publisher, _relogio),
            new DefinirControleExpedienteUseCase(_repo, uow, publisher, _relogio, _auditoria, Substitute.For<INotificadorService>()),
            _currentUser);
    }

    [Fact]
    public void Politicas_HorarioDaDona_ControleDeGerente()
    {
        typeof(AtendimentoExpedienteController).GetCustomAttributes<AuthorizeAttribute>()
            .Should().OnlyContain(a => a.Policy == null, "política na classe somaria (E) com a do controle e travaria o gerente");

        Politica(nameof(AtendimentoExpedienteController.Get)).Should().Be("Admin");
        Politica(nameof(AtendimentoExpedienteController.Put)).Should().Be("Admin");
        Politica(nameof(AtendimentoExpedienteController.DefinirControle)).Should().Be("Gerente");
    }

    [Fact]
    public async Task FecharNoHorario_GerenteComJustificativa_GravaAuditoria()
    {
        var resposta = await Controller(NivelAcesso.Gerente).DefinirControle(
            new DefinirControleExpedienteBody(ControleManualLoja.ForcarFechada, "Falta de luz no bairro"), CancellationToken.None);

        resposta.Should().BeOfType<OkObjectResult>();
        await _auditoria.Received(1).AddAsync(Arg.Is<EasyStock.Domain.Entities.AuditLog>(a => a.UsuarioId == _usuarioId));
    }

    [Fact]
    public async Task FecharNoHorario_SemJustificativa_Devolve400()
    {
        var resposta = await Controller(NivelAcesso.Admin).DefinirControle(
            new DefinirControleExpedienteBody(ControleManualLoja.ForcarFechada), CancellationToken.None);

        resposta.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task VoltarAoHorario_Gerente_Devolve403ComMotivo()
    {
        var resposta = await Controller(NivelAcesso.Gerente).DefinirControle(
            new DefinirControleExpedienteBody(ControleManualLoja.Automatico), CancellationToken.None);

        var objeto = resposta.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        objeto.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.Error.Message.Should().Contain("dona");
    }

    private static string? Politica(string acao) =>
        typeof(AtendimentoExpedienteController).GetMethod(acao)!
            .GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).SingleOrDefault();
}
