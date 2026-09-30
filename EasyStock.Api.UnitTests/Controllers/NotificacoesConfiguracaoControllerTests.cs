using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// P01-B (#1173/#1176): templates, rotinas e canais de notificação no contexto da empresa. A empresa
/// sai do token; recurso de outra empresa ou global responde 404 e nada é gravado.
/// </summary>
public class NotificacoesConfiguracaoControllerTests
{
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IRotinaRepository _rotinas = Substitute.For<IRotinaRepository>();
    private readonly IConfiguracaoCanalRepository _canais = Substitute.For<IConfiguracaoCanalRepository>();
    private readonly IBloqueioNotificacaoRepository _bloqueios = Substitute.For<IBloqueioNotificacaoRepository>();
    private readonly IVariavelTemplateCatalogoRepository _variaveis = Substitute.For<IVariavelTemplateCatalogoRepository>();
    private readonly ILogEnvioNotificacaoRepository _logs = Substitute.For<ILogEnvioNotificacaoRepository>();
    private readonly IRendererTemplate _renderer = Substitute.For<IRendererTemplate>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private readonly Guid _empresaA = Guid.NewGuid();
    private readonly Guid _empresaB = Guid.NewGuid();
    private readonly TemplateNotificacao _templateA;
    private readonly TemplateNotificacao _templateB;
    private readonly TemplateNotificacao _templateGlobal;
    private readonly RotinaNotificacao _rotinaA;
    private readonly RotinaNotificacao _rotinaB;
    private readonly BloqueioNotificacao _bloqueioA;
    private readonly BloqueioNotificacao _bloqueioB;
    private readonly BloqueioNotificacao _bloqueioGlobal;
    private readonly NotificacoesConfiguracaoController _controller;

    public NotificacoesConfiguracaoControllerTests()
    {
        _templateA = Template(_empresaA);
        _templateB = Template(_empresaB);
        _templateGlobal = Template(null);
        foreach (var t in new[] { _templateA, _templateB, _templateGlobal })
            _templates.GetByIdAsync(t.Id, Arg.Any<CancellationToken>()).Returns(t);

        _rotinaA = Rotina(_empresaA);
        _rotinaB = Rotina(_empresaB);
        foreach (var r in new[] { _rotinaA, _rotinaB })
            _rotinas.GetByIdAsync(r.Id, Arg.Any<CancellationToken>()).Returns(r);

        _bloqueioA = BloqueioNotificacao.Criar("pausa", "a", _empresaA);
        _bloqueioB = BloqueioNotificacao.Criar("pausa", "b", _empresaB);
        _bloqueioGlobal = BloqueioNotificacao.Criar("incidente", "plataforma");
        foreach (var b in new[] { _bloqueioA, _bloqueioB, _bloqueioGlobal })
            _bloqueios.GetByIdAsync(b.Id, Arg.Any<CancellationToken>()).Returns(b);

        _currentUser.EmpresaId.Returns(_empresaA);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());

        _controller = new NotificacoesConfiguracaoController(
            _templates, _rotinas, _canais, _bloqueios, _variaveis, _currentUser,
            new CriarTemplateUseCase(_templates, _uow, NullLogger<CriarTemplateUseCase>.Instance),
            new AtualizarTemplateUseCase(_templates, _uow, NullLogger<AtualizarTemplateUseCase>.Instance),
            new AprovarTemplateUseCase(_templates, _uow),
            new PreviewTemplateUseCase(_templates, _renderer),
            new PreviewDraftTemplateUseCase(_renderer),
            new CriarRotinaUseCase(_rotinas, _uow, NullLogger<CriarRotinaUseCase>.Instance),
            new AtualizarRotinaUseCase(_rotinas, _uow),
            new AtivarRotinaUseCase(_rotinas, _uow),
            new DesativarRotinaUseCase(_rotinas, _uow),
            new AtivarKillSwitchUseCase(_bloqueios, _uow, NullLogger<AtivarKillSwitchUseCase>.Instance),
            new RemoverKillSwitchUseCase(_bloqueios, _uow),
            new ListarLogsEnvioUseCase(_logs));
    }

    private static TemplateNotificacao Template(Guid? empresaId) =>
        TemplateNotificacao.Criar("pedido_pronto", "Pedido pronto", CanalNotificacao.Email,
            TipoEventoNotificacao.TarefaPendente, "Assunto", "Corpo", empresaId);

    private static RotinaNotificacao Rotina(Guid empresaId) =>
        RotinaNotificacao.Criar("lembrete", "Lembrete", TipoEventoNotificacao.TarefaPendente,
            TriggerTipoRotina.Evento, "pedido_pronto", CategoriaConteudoNotificacao.Transacional,
            empresaId: empresaId);

    // ── Templates ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarTemplates_filtra_pela_empresa_do_token()
    {
        _templates.ListarAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(((IReadOnlyList<TemplateNotificacao>)[_templateA], 1));

        (await _controller.ListarTemplates(null, null, null)).Should().BeOfType<OkObjectResult>();

        await _templates.Received(1).ListarAsync(_empresaA, null, null, null, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTemplate_de_outra_empresa_ou_global_devolve_404()
    {
        (await _controller.GetTemplate(_templateA.Id)).Should().BeOfType<OkObjectResult>();
        (await _controller.GetTemplate(_templateB.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.GetTemplate(_templateGlobal.Id)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CriarTemplate_grava_na_empresa_do_token()
    {
        var req = new NotificacoesConfiguracaoController.CriarTemplateRequest(
            "boas_vindas", "Boas-vindas", CanalNotificacao.Email, TipoEventoNotificacao.TarefaPendente,
            "Oi", "Olá {{nome}}");

        (await _controller.CriarTemplate(req)).Should().BeOfType<OkObjectResult>();

        await _templates.Received(1).AddAsync(
            Arg.Is<TemplateNotificacao>(t => t.EmpresaId == _empresaA && t.Codigo == "boas_vindas"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtualizarTemplate_de_outra_empresa_ou_global_devolve_404_sem_gravar()
    {
        var req = new NotificacoesConfiguracaoController.AtualizarTemplateRequest("x", "y");

        (await _controller.AtualizarTemplate(_templateB.Id, req)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.AtualizarTemplate(_templateGlobal.Id, req)).Should().BeOfType<NotFoundObjectResult>();

        _templateB.CorpoTemplate.Should().Be("Corpo");
        await _templates.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task AtualizarTemplate_da_propria_empresa_cria_nova_versao_na_empresa()
    {
        var req = new NotificacoesConfiguracaoController.AtualizarTemplateRequest("Novo", "Novo corpo");

        (await _controller.AtualizarTemplate(_templateA.Id, req)).Should().BeOfType<OkObjectResult>();

        await _templates.Received(1).AddAsync(
            Arg.Is<TemplateNotificacao>(t => t.EmpresaId == _empresaA && t.Versao == 2),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AprovarTemplate_de_outra_empresa_ou_global_devolve_404()
    {
        (await _controller.AprovarTemplate(_templateB.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.AprovarTemplate(_templateGlobal.Id)).Should().BeOfType<NotFoundObjectResult>();

        _templateB.Aprovado.Should().BeFalse();
        _templateGlobal.Aprovado.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PreviewTemplate_de_outra_empresa_devolve_404()
    {
        var req = new NotificacoesConfiguracaoController.PreviewTemplateRequest(_templateB.Id);

        (await _controller.PreviewTemplate(req)).Should().BeOfType<NotFoundObjectResult>();

        await _renderer.DidNotReceiveWithAnyArgs().RenderizarAsync(default!, default!);
    }

    // ── Rotinas ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarRotinas_filtra_pela_empresa_do_token()
    {
        _rotinas.ListarAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(((IReadOnlyList<RotinaNotificacao>)[_rotinaA], 1));

        (await _controller.ListarRotinas(null)).Should().BeOfType<OkObjectResult>();

        await _rotinas.Received(1).ListarAsync(_empresaA, null, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rotina_de_outra_empresa_nao_e_lida_nem_alterada()
    {
        (await _controller.GetRotina(_rotinaA.Id)).Should().BeOfType<OkObjectResult>();
        (await _controller.GetRotina(_rotinaB.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.AtualizarRotina(_rotinaB.Id, new("0 0 * * *", null))).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.AtivarRotina(_rotinaB.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.DesativarRotina(_rotinaB.Id)).Should().BeOfType<NotFoundObjectResult>();

        await _rotinas.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CriarRotina_grava_na_empresa_do_token()
    {
        var req = new NotificacoesConfiguracaoController.CriarRotinaRequest(
            "aviso", "Aviso", TipoEventoNotificacao.TarefaPendente, TriggerTipoRotina.Evento,
            "pedido_pronto", CategoriaConteudoNotificacao.Transacional);

        (await _controller.CriarRotina(req)).Should().BeOfType<OkObjectResult>();

        await _rotinas.Received(1).AddAsync(
            Arg.Is<RotinaNotificacao>(r => r.EmpresaId == _empresaA), Arg.Any<CancellationToken>());
    }

    // ── Canais / kill switch / envios ───────────────────────────────────────

    [Fact]
    public async Task ListarCanais_usa_a_empresa_do_token()
    {
        (await _controller.ListarCanais()).Should().BeOfType<OkObjectResult>();

        await _canais.Received(1).ListarAsync(_empresaA, Arg.Any<CancellationToken>());
        await _bloqueios.Received(1).ListarAtivosAsync(_empresaA, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KillSwitch_bloqueia_so_a_empresa_do_token()
    {
        var req = new NotificacoesConfiguracaoController.KillSwitchRequest("manutenção", "Email");

        (await _controller.AtivarKillSwitch(req)).Should().BeOfType<OkObjectResult>();

        await _bloqueios.Received(1).AddAsync(
            Arg.Is<BloqueioNotificacao>(b => b.EmpresaId == _empresaA && b.Canal == CanalNotificacao.Email),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoverKillSwitch_de_outra_empresa_ou_global_devolve_404()
    {
        (await _controller.RemoverKillSwitch(_bloqueioB.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.RemoverKillSwitch(_bloqueioGlobal.Id)).Should().BeOfType<NotFoundObjectResult>();
        _bloqueioB.RemovidoEm.Should().BeNull();
        _bloqueioGlobal.RemovidoEm.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();

        (await _controller.RemoverKillSwitch(_bloqueioA.Id)).Should().BeOfType<OkObjectResult>();
        _bloqueioA.RemovidoEm.Should().NotBeNull();
    }

    [Fact]
    public async Task ListarEnvios_filtra_pela_empresa_do_token()
    {
        _logs.ListarAsync(default, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(((IReadOnlyList<LogEnvioNotificacao>)[], 0));

        (await _controller.ListarEnvios(null, null, null, null)).Should().BeOfType<OkObjectResult>();

        await _logs.Received(1).ListarAsync(_empresaA, null, null, null, null, 1, 20, Arg.Any<CancellationToken>());
    }

    // ── Contrato ────────────────────────────────────────────────────────────

    [Fact]
    public void Sessao_sem_empresa_devolve_400_antes_da_acao()
    {
        _currentUser.EmpresaId.Returns(Guid.Empty);
        var contexto = new ActionExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), _controller);

        ((IActionFilter)_controller).OnActionExecuting(contexto);

        contexto.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Contrato_exige_policy_Admin_e_nao_aceita_empresaId_do_cliente()
    {
        var tipo = typeof(NotificacoesConfiguracaoController);
        tipo.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("Admin");

        tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.Name!.ToLowerInvariant())
            .Should().NotContain("empresaid");

        tipo.GetNestedTypes()
            .SelectMany(t => t.GetProperties())
            .Select(p => p.Name)
            .Should().NotContain("EmpresaId");
    }
}
