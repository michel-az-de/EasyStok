using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Notifications.Plataforma;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Notifications.Plataforma;

/// <summary>N6: quem escreve ao número de plataforma recebe um aviso de "não monitorado", uma vez por dia, sem Conversa.</summary>
public class ResponderMensagemRecebidaPlataformaUseCaseTests
{
    private readonly IClienteWhatsAppPlataforma _cliente = Substitute.For<IClienteWhatsAppPlataforma>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    public ResponderMensagemRecebidaPlataformaUseCaseTests()
    {
        _cliente.EnviarTextoPlataformaAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioPlataforma(DesfechoEnvio.Enviado, "wamid.R"));
    }

    private ResponderMensagemRecebidaPlataformaUseCase Sut() =>
        new(_cliente, _cache, NullLogger<ResponderMensagemRecebidaPlataformaUseCase>.Instance);

    private static MensagemRecebidaPlataforma Msg(string tipo = "text", string de = "5511999990001") =>
        new("7770009999", de, "wamid.IN", tipo);

    [Fact]
    public async Task RespondeUmaVezPorRemetenteEm24h()
    {
        var chamadas = new Dictionary<string, long>();
        _cache.IncrementAsync(Arg.Any<string>(), Arg.Any<long>()).Returns(c =>
        {
            var k = c.Arg<string>();
            chamadas[k] = chamadas.GetValueOrDefault(k) + 1;
            return chamadas[k];
        });

        await Sut().ExecuteAsync(Msg());
        await Sut().ExecuteAsync(Msg());
        await Sut().ExecuteAsync(Msg(de: "5511888880002"));

        await _cliente.Received(2).EnviarTextoPlataformaAsync(
            Arg.Any<string>(), Arg.Is<string>(t => t.Contains("não é monitorado")), Arg.Any<CancellationToken>());
        await _cliente.Received(1).EnviarTextoPlataformaAsync("5511999990001", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cache.Received(2).SetExpiryAsync(Arg.Any<string>(), TimeSpan.FromHours(24));
        // O número do remetente nunca vai cru para a chave do cache.
        chamadas.Keys.Should().OnlyContain(k => !k.Contains("5511999990001"));
    }

    [Theory]
    [InlineData("reaction")]
    [InlineData("system")]
    [InlineData("unsupported")]
    public async Task NaoRespondeReacaoNemSistema(string tipo)
    {
        await Sut().ExecuteAsync(Msg(tipo));

        await _cliente.DidNotReceiveWithAnyArgs().EnviarTextoPlataformaAsync(default!, default!, default);
        await _cache.DidNotReceiveWithAnyArgs().IncrementAsync(default!, default);
    }

    [Fact]
    public void NaoGravaConversaNemMensagem()
    {
        // Fronteira estrutural: o caso de uso só conhece o cliente de plataforma e o cache.
        typeof(ResponderMensagemRecebidaPlataformaUseCase).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType.Namespace)
            .Should().NotContain(n => n != null && n.Contains("Atendimento"));
    }
}
