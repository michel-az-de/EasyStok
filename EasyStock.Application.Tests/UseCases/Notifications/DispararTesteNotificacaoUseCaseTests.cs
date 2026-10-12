using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Notifications;

/// <summary>
/// N13: o disparo de teste publica o exemplo do tipo pelo caminho real (<c>EnfileirarEventoAsync</c>) e o destinatário é
/// sempre o contato do próprio superadmin que chamou, lido de <c>Usuario</c>, nunca do corpo.
/// </summary>
public class DispararTesteNotificacaoUseCaseTests
{
    private readonly ICurrentUserAccessor _usuarioAtual = Substitute.For<ICurrentUserAccessor>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IEmpresaPadraoResolver _empresaPadrao = Substitute.For<IEmpresaPadraoResolver>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IEventoNotificacaoRepository _eventos = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IOutboxNotificacaoRepository _outbox = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Guid _superadminId = Guid.NewGuid();
    private readonly Guid _eventoId = Guid.NewGuid();
    private readonly Usuario _superadmin = Usuario.Criar("Felipe", "felipe@example.com", "hash");

    public DispararTesteNotificacaoUseCaseTests()
    {
        _superadmin.Id = _superadminId;
        _superadmin.EmailConfirmado = true;
        _usuarioAtual.UsuarioId.Returns(_superadminId);
        _usuarios.GetByIdAsync(_superadminId).Returns(_superadmin);
        _notificador.EnfileirarEventoAsync(Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_eventoId);
    }

    private DispararTesteNotificacaoUseCase Criar() => new(
        _usuarioAtual, _usuarios, _empresaPadrao, _notificador, _eventos, _outbox, _tenant, _unitOfWork,
        NullLogger<DispararTesteNotificacaoUseCase>.Instance);

    private string PayloadPublicado() =>
        (string)_notificador.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync))
            .GetArguments()[2]!;

    [Fact]
    public async Task UsaOEmailDoSuperadminQueChamou()
    {
        var empresa = Guid.NewGuid();
        _usuarioAtual.EmpresaId.Returns(empresa);

        await Criar().ExecuteAsync(TipoEventoNotificacao.IncidenteSistema);

        var payload = JsonDocument.Parse(PayloadPublicado()).RootElement;
        payload.GetProperty("email").GetString().Should().Be("felipe@example.com");
        payload.GetProperty("usuarioId").GetString().Should().Be(_superadminId.ToString());
    }

    [Fact]
    public async Task PublicaOExemploDoTipoComTesteVerdadeiro()
    {
        _usuarioAtual.EmpresaId.Returns(Guid.NewGuid());

        var eventoId = await Criar().ExecuteAsync(TipoEventoNotificacao.IncidenteSistema);

        eventoId.Should().Be(_eventoId);
        var payload = JsonDocument.Parse(PayloadPublicado()).RootElement;
        payload.GetProperty("teste").GetBoolean().Should().BeTrue();
        foreach (var chave in ExemplosDeEvento.Obter(TipoEventoNotificacao.IncidenteSistema).Keys)
            payload.TryGetProperty(chave, out _).Should().BeTrue($"o exemplo leva a chave {chave}");
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task RecusaTipoForaDoCatalogo()
    {
        var acao = () => Criar().ExecuteAsync(TipoEventoNotificacao.PedidoEntregue);

        var erro = (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which;
        erro.Code.Should().Be("TIPO_FORA_DO_CATALOGO");
        erro.Message.Should().Contain("IncidenteSistema").And.Contain("ResetSenha");
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task UsaAEmpresaDoTokenOuAPadrao()
    {
        var doToken = Guid.NewGuid();
        var padrao = Guid.NewGuid();
        _empresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns(padrao);

        _usuarioAtual.EmpresaId.Returns(doToken);
        await Criar().ExecuteAsync(TipoEventoNotificacao.ResumoDiario);
        _usuarioAtual.EmpresaId.Returns(Guid.Empty);
        await Criar().ExecuteAsync(TipoEventoNotificacao.ResumoDiario);

        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.ResumoDiario, doToken, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.ResumoDiario, padrao, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        _tenant.Received(1).SetCurrentTenant(doToken);
        _tenant.Received(1).SetCurrentTenant(padrao);
    }

    [Fact]
    public async Task SemEmpresaResolvidaNaoPublicaENomeiaAChave()
    {
        _usuarioAtual.EmpresaId.Returns(Guid.Empty);
        _empresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var acao = () => Criar().ExecuteAsync(TipoEventoNotificacao.ResumoDiario);

        var erro = (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which;
        erro.Code.Should().Be("EMPRESA_PADRAO_NAO_RESOLVIDA");
        erro.Message.Should().Contain("Auth:Google:EmpresaPadrao");
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task SuperadminSemEmailConfirmadoNaoPublica()
    {
        _usuarioAtual.EmpresaId.Returns(Guid.NewGuid());
        _superadmin.EmailConfirmado = false;

        var acao = () => Criar().ExecuteAsync(TipoEventoNotificacao.IncidenteSistema);

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("EMAIL_NAO_CONFIRMADO");
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task ConsultaMostraCanalStatusProviderEErroCurtoSemCorpo()
    {
        var empresa = Guid.NewGuid();
        _usuarioAtual.EmpresaId.Returns(empresa);
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.IncidenteSistema, empresa, "{}");
        evento.MarcarComoProcessado();
        _eventos.ObterAsync(empresa, evento.Id, Arg.Any<CancellationToken>()).Returns(evento);
        var mensagem = OutboxMensagemNotificacao.Criar(evento.Id, Guid.NewGuid(), empresa, CanalNotificacao.Email,
            "felipe@example.com", "[TESTE] Assunto", "CORPO SECRETO", CategoriaConteudoNotificacao.Seguranca);
        mensagem.ErroUltimaTentativa = new string('x', 500);
        _outbox.ListarDoEventoAsync(empresa, evento.Id, Arg.Any<CancellationToken>()).Returns([mensagem]);

        var resultado = await Criar().ConsultarAsync(evento.Id);

        resultado.Should().NotBeNull();
        resultado!.EventoStatus.Should().Be("Processado");
        var item = resultado.Mensagens.Should().ContainSingle().Subject;
        item.Canal.Should().Be("Email");
        item.Status.Should().Be("Pendente");
        item.Erro!.Length.Should().BeLessThanOrEqualTo(200);
        JsonSerializer.Serialize(resultado).Should().NotContain("CORPO SECRETO").And.NotContain("felipe@example.com");
    }

    [Fact]
    public async Task ConsultaDeEventoDeOutraEmpresaNaoAcha()
    {
        _usuarioAtual.EmpresaId.Returns(Guid.NewGuid());

        var resultado = await Criar().ConsultarAsync(Guid.NewGuid());

        resultado.Should().BeNull();
    }
}
