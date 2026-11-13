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

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>
/// #2072 (ADR-0062): com a ponte não oficial o canal não tem modelo aprovado. Onde a Meta mandaria o modelo, sai o texto
/// da mensagem; sem modelo, fora da janela continua recusado: a ponte manda o que a Meta mandaria, nada a mais.
/// </summary>
public class MetaCloudWhatsAppProviderPonteTests
{
    private const string Telefone = "5511999990001";
    private const string Corpo = "Seu pedido #123 saiu para entrega.";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria, ICanalSemModeloAprovado>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();

    public MetaCloudWhatsAppProviderPonteTests()
    {
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        _canal.EnviarTextoAsync(default!, default!, default).ReturnsForAnyArgs("true_5511999990001@c.us_A");
        _canal.EnviarBotoesAsync(default!, default!, default!, default).ReturnsForAnyArgs("true_5511999990001@c.us_B");
    }

    private MetaCloudWhatsAppProvider Provider() => new(
        new ResolvedorCanal([_canal]), _conversas, Substitute.For<ITenantContextAccessor>(), Substitute.For<IUnitOfWork>(),
        NullLogger<MetaCloudWhatsAppProvider>.Instance);

    private MensagemPronta Mensagem(Dictionary<string, string>? metadados) => new(
        Guid.NewGuid(), _empresaId, "+" + Telefone, "Seu pedido", Corpo,
        CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Transacional) { Metadados = metadados };

    private static Dictionary<string, string> ComModelo(params (string Chave, string Valor)[] extras)
    {
        var metadados = new Dictionary<string, string> { ["template"] = "pedido_saiu", ["idioma"] = "pt_BR", ["param1"] = "#123" };
        foreach (var (chave, valor) in extras) metadados[chave] = valor;
        return metadados;
    }

    private void ConversaComEntradaHa(TimeSpan atras)
    {
        var agora = DateTime.UtcNow;
        var conversa = Conversa.Abrir(_empresaId, Telefone, agora - atras - TimeSpan.FromMinutes(1));
        conversa.RegistrarEntrada(agora - atras);
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(conversa);
    }

    [Fact]
    public async Task ForaDaJanelaComModeloMandaOTextoDaMensagem()
    {
        ConversaComEntradaHa(TimeSpan.FromHours(30));

        var resultado = await Provider().EnviarAsync(Mensagem(ComModelo()));

        resultado.Sucesso.Should().BeTrue();
        await _canal.Received(1).EnviarTextoAsync(Telefone, Corpo, Arg.Any<CancellationToken>());
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task SemConversaComModeloMandaOTextoDaMensagem()
    {
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Conversa?)null);

        var resultado = await Provider().EnviarAsync(Mensagem(ComModelo()));

        resultado.Sucesso.Should().BeTrue();
        await _canal.Received(1).EnviarTextoAsync(Telefone, Corpo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForaDaJanelaComModeloEBotoesMandaOsBotoes()
    {
        ConversaComEntradaHa(TimeSpan.FromHours(30));

        await Provider().EnviarAsync(Mensagem(ComModelo(("botao1", "acao:avaliacao:positiva:abc|Gostei"), ("botao2", "acao:avaliacao:negativa:abc|Não gostei"))));

        await _canal.Received(1).EnviarBotoesAsync(Telefone, Corpo,
            Arg.Is<IReadOnlyList<(string Id, string Titulo)>>(b => b.Count == 2 && b[1].Id == "acao:avaliacao:negativa:abc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForaDaJanelaSemModeloContinuaRecusado()
    {
        ConversaComEntradaHa(TimeSpan.FromHours(30));

        var resultado = await Provider().EnviarAsync(Mensagem(null));

        resultado.Sucesso.Should().BeFalse();
        resultado.ErroDetalhado.Should().Be(MetaCloudWhatsAppProvider.ErroForaDaJanelaSemTemplate);
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }
}
