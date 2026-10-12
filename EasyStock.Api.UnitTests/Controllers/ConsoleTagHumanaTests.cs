using Microsoft.Extensions.Logging.Abstractions;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Integrations.Meta;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Resposta da dona fora da janela no Instagram e no Messenger (S35): até 7 dias depois da última
/// mensagem do cliente sai com <c>HUMAN_AGENT</c>; depois disso é 409 como no WhatsApp, que continua
/// exigindo modelo.
/// </summary>
public class ConsoleTagHumanaTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly StubMetaMensageriaTransporte _meta = new();
    private readonly ICanalMensageria _whatsApp = Substitute.For<ICanalMensageria>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AtendimentoConversasController _controller;

    public ConsoleTagHumanaTests()
    {
        _whatsApp.Canal.Returns(CanalConversa.WhatsApp);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.EmpresaId.Returns(_empresaId);
        currentUser.UsuarioId.Returns(Guid.NewGuid());
        currentUser.TemPermissao(Permissao.AtenderConversas).Returns(true);
        var uploads = new GerenciarUploadsUseCase(
            Substitute.For<IFileStorage>(), Substitute.For<IImageProcessor>(), Substitute.For<IProdutoRepository>(),
            Substitute.For<IUsuarioRepository>(), Substitute.For<ILojaRepository>(), Substitute.For<IStorefrontRepository>(),
            Substitute.For<ICardapioItemRepository>(), _uow);
        var resolvedor = new ResolvedorCanal([_whatsApp, new CanalMessenger(_meta), new CanalInstagram(_meta)]);

        _controller = new AtendimentoConversasController(
            new ListarConversasAtendimentoUseCase(_conversas, ConfiguracoesPadrao(), Microsoft.Extensions.Options.Options.Create(new EasyStock.Application.Services.Notifications.PrazosOptions())),
            new ListarMensagensConversaUseCase(_conversas),
            new EnviarMensagemConsoleUseCase(_conversas, resolvedor, uploads, _uow),
            new ReenviarMensagemUseCase(_conversas, ConfiguracoesPadrao(), resolvedor,
                new ReservaSmsAtendimento(resolvedor, ReservaSmsOpcoes.Desligada, NullLogger<ReservaSmsAtendimento>.Instance),
                _uow, TimeProvider.System),
            new ListarNaoEntreguesUseCase(_conversas),
            new GerenciarConversaAtendimentoUseCase(_conversas, _uow),
            new TransferirConversaUseCase(_conversas, Substitute.For<IAtendenteRepository>(), _uow),
            new ObterDossieClienteUseCase(
                Substitute.For<IClienteRepository>(), Substitute.For<IClienteCrmRepository>(),
                Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _conversas),
            new GerarLinkCardapioConversaUseCase(_conversas,
                AtendimentoConversasControllerTests.LinkCardapio(Substitute.For<ILinkCardapioConversaRepository>()), _uow, TimeProvider.System),
            currentUser);
    }

    private Conversa Conversa(CanalConversa canal, string contato, TimeSpan desdeUltimaEntrada)
    {
        var entrada = DateTime.UtcNow - desdeUltimaEntrada;
        var conversa = Domain.Entities.Atendimento.Conversa.Abrir(_empresaId, contato, entrada.AddMinutes(-1), canal: canal);
        conversa.RegistrarEntrada(entrada);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        return conversa;
    }

    [Theory]
    [InlineData(CanalConversa.Messenger, "PSID-3")]
    [InlineData(CanalConversa.Instagram, "IGSID-9")]
    public async Task VinteECincoHoras_DonaRespondeComHumanAgent(CanalConversa canal, string contato)
    {
        var conversa = Conversa(canal, contato, TimeSpan.FromHours(25));

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("Voltei, desculpe a demora!"), default);

        result.Should().BeOfType<OkObjectResult>();
        _meta.Enviados.Single().Should().Contain("\"messaging_type\":\"MESSAGE_TAG\"").And.Contain("\"tag\":\"HUMAN_AGENT\"")
            .And.Contain(contato);
        await _uow.Received(1).CommitAsync();
    }

    private static IConfiguracaoAtendimentoRepository ConfiguracoesPadrao()
    {
        var configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
        configuracoes.GetOrDefaultAsync(Arg.Any<Guid>()).Returns(c => ConfiguracaoAtendimento.CriarPadrao(c.Arg<Guid>()));
        configuracoes.GetByEmpresaIdAsync(Arg.Any<Guid>()).Returns(c => ConfiguracaoAtendimento.CriarPadrao(c.Arg<Guid>()));
        return configuracoes;
    }

    [Fact]
    public async Task DentroDaJanela_SemTag()
    {
        var conversa = Conversa(CanalConversa.Messenger, "PSID-3", TimeSpan.FromHours(1));

        await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("Oi!"), default);

        _meta.Enviados.Single().Should().Contain("\"messaging_type\":\"RESPONSE\"").And.NotContain("HUMAN_AGENT");
    }

    [Fact]
    public async Task OitoDias_Recusa409SemEnviar()
    {
        var conversa = Conversa(CanalConversa.Instagram, "IGSID-9", TimeSpan.FromDays(8));

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("oi"), default);

        result.Should().BeOfType<ConflictObjectResult>();
        _meta.Enviados.Should().BeEmpty();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task WhatsAppForaDaJanela_ContinuaExigindoModelo()
    {
        var conversa = Conversa(CanalConversa.WhatsApp, "5511999998888", TimeSpan.FromHours(25));

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("oi"), default);

        result.Should().BeOfType<ConflictObjectResult>();
        await _whatsApp.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }
    [Fact]
    public async Task WhatsAppTimeoutDoHttpClient_GravaMensagemComoFalhou()
    {
        // #1411: o timeout do HttpClient chega como TaskCanceledException sem o ct cancelado; a mensagem não pode sumir.
        var conversa = Conversa(CanalConversa.WhatsApp, "5511999997777", TimeSpan.FromHours(1));
        _whatsApp.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new TaskCanceledException("timeout"));

        await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("oi"), default);

        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Status == StatusMensagem.Falhou), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }
}
