using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>
/// S09: o provider da Meta no outbox decide entre texto (dentro da janela de 24 h) e template
/// (sem conversa aberta ou fora da janela), envia pela porta de canal (S34) e copia a saída para o
/// histórico da conversa aberta como <c>Mensagem(Saida, Sistema)</c>.
/// </summary>
public class MetaCloudWhatsAppProviderTests
{
    private const string Telefone = "5511999990001";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public MetaCloudWhatsAppProviderTests()
    {
        _canal.Canal.Returns(CanalConversa.WhatsApp);
    }

    private MetaCloudWhatsAppProvider Provider() => new(
        new ResolvedorCanal([_canal]), _conversas, _tenant, _uow, NullLogger<MetaCloudWhatsAppProvider>.Instance);

    private MensagemPronta Mensagem(IReadOnlyDictionary<string, string>? metadados = null) => new(
        Guid.NewGuid(), _empresaId, "+" + Telefone, "Seu pedido", "Seu pedido #123 foi pago.",
        CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Transacional) { Metadados = metadados };

    private Conversa ConversaComEntradaHa(TimeSpan atras)
    {
        var agora = DateTime.UtcNow;
        var conversa = Conversa.Abrir(_empresaId, Telefone, agora - atras - TimeSpan.FromMinutes(1));
        conversa.RegistrarEntrada(agora - atras);
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(conversa);
        return conversa;
    }

    private static readonly Dictionary<string, string> ComTemplate = new()
    {
        ["template"] = "pedido_pago",
        ["idioma"] = "pt_BR",
        ["param2"] = "R$ 45,00",
        ["param1"] = "#123",
    };

    [Fact]
    public async Task DentroDaJanelaTexto()
    {
        var conversa = ConversaComEntradaHa(TimeSpan.FromHours(1));
        _canal.EnviarTextoAsync(Telefone, "Seu pedido #123 foi pago.", Arg.Any<CancellationToken>()).Returns("wamid.texto");

        var resultado = await Provider().EnviarAsync(Mensagem(ComTemplate));

        resultado.Sucesso.Should().BeTrue();
        resultado.ProviderUsado.Should().Be("meta");
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
        _tenant.Received().SetCurrentTenant(_empresaId);
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ConversaId == conversa.Id && m.Direcao == DirecaoMensagem.Saida
                && m.Autor == AutorMensagem.Sistema && m.ExternoId == "wamid.texto"),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ForaDaJanelaTemplate()
    {
        var conversa = ConversaComEntradaHa(TimeSpan.FromHours(25));
        _canal.EnviarModeloAsync(Telefone, "pedido_pago", "pt_BR",
                Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "#123", "R$ 45,00" })), Arg.Any<CancellationToken>())
            .Returns("wamid.template");

        var resultado = await Provider().EnviarAsync(Mensagem(ComTemplate));

        resultado.Sucesso.Should().BeTrue();
        resultado.ProviderUsado.Should().Be("meta");
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ConversaId == conversa.Id && m.Autor == AutorMensagem.Sistema && m.ExternoId == "wamid.template"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemTemplateFalhaPermanente()
    {
        ConversaComEntradaHa(TimeSpan.FromHours(25));

        var resultado = await Provider().EnviarAsync(Mensagem());

        resultado.Sucesso.Should().BeFalse();
        resultado.FalhaPermanente.Should().BeTrue();
        resultado.ErroDetalhado.Should().Be("fora_da_janela_24h_sem_template");
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task SemConversaEnviaTemplateENaoGravaMensagem()
    {
        _canal.EnviarModeloAsync(Telefone, "pedido_pago", "pt_BR", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("wamid.template");

        var resultado = await Provider().EnviarAsync(Mensagem(ComTemplate));

        resultado.Sucesso.Should().BeTrue();
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task SemConversaSemTemplateMetaRecusaForaDaJanelaFalhaPermanente()
    {
        _canal.EnviarTextoAsync(Telefone, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new WhatsAppCloudException(131047, "Re-engagement message", ehPermanente: false));

        var resultado = await Provider().EnviarAsync(Mensagem());

        resultado.Sucesso.Should().BeFalse();
        resultado.FalhaPermanente.Should().BeTrue();
        resultado.ErroDetalhado.Should().Be("fora_da_janela_24h_sem_template");
    }

    [Fact]
    public async Task ErroTransitorioNaoEPermanente()
    {
        _canal.EnviarTextoAsync(Telefone, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var resultado = await Provider().EnviarAsync(Mensagem());

        resultado.Sucesso.Should().BeFalse();
        resultado.FalhaPermanente.Should().BeFalse();
    }
}
