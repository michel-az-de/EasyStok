using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Atendimento;
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
        var uploads = new GerenciarUploadsUseCase(
            Substitute.For<IFileStorage>(), Substitute.For<IImageProcessor>(), Substitute.For<IProdutoRepository>(),
            Substitute.For<IUsuarioRepository>(), Substitute.For<ILojaRepository>(), Substitute.For<IStorefrontRepository>(),
            Substitute.For<ICardapioItemRepository>(), _uow);
        var resolvedor = new ResolvedorCanal([_whatsApp, new CanalMessenger(_meta), new CanalInstagram(_meta)]);

        _controller = new AtendimentoConversasController(
            new ListarConversasAtendimentoUseCase(_conversas),
            new ListarMensagensConversaUseCase(_conversas),
            new EnviarMensagemConsoleUseCase(_conversas, resolvedor, uploads, _uow),
            new GerenciarConversaAtendimentoUseCase(_conversas, _uow),
            new TransferirConversaUseCase(_conversas, Substitute.For<IAtendenteRepository>(), _uow),
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
}
