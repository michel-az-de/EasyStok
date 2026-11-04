using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>#2096: <c>avisar_gestao</c> avisa a dona e o gerente da reclamação ou suspeita, sem contato no Push.</summary>
public class AvisarGestaoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IEventoNotificacaoRepository _eventos = Substitute.For<IEventoNotificacaoRepository>();

    private AvisarGestaoFerramenta Criar() => new(_notificador, _eventos, Options.Create(new PrazosOptions()));

    private ContextoTurnoAgente Contexto(string? nome = "Maria Souza") =>
        new(_empresaId, Conversa.Abrir(_empresaId, "5511999990000", Agora.AddMinutes(-3), nome), Agora);

    private static JsonElement Entrada(string tipo, string resumo) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { tipo, resumo })).RootElement;

    private JsonElement Payload() =>
        JsonDocument.Parse((string)_notificador.ReceivedCalls().Single().GetArguments()[2]!).RootElement;

    [Fact]
    public async Task ReclamacaoViraAlertaComPushSemDetalhe()
    {
        var contexto = Contexto();

        var r = await Criar().ExecutarAsync(contexto, Entrada("reclamacao", "Disse que a lasanha chegou fria"));

        r.Should().Contain("avisado");
        await _notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.AlertaOperacao, _empresaId,
            Arg.Any<string>(), contexto.Conversa.Id, Arg.Any<CancellationToken>(), $"av:rc:{contexto.Conversa.Id:N}:261027");
        var p = Payload();
        p.GetProperty("aviso").GetString().Should().Be(CodigosAviso.ReclamacaoCliente);
        p.GetProperty("resumo_curto").GetString().Should().Be("Maria está reclamando no Balcão");
        p.GetProperty("texto").GetString().Should().Be("Reclamação na conversa de Maria: Disse que a lasanha chegou fria");
        p.GetProperty("link").GetString().Should().EndWith("/#/m/atendimento/balcao");
    }

    [Fact]
    public async Task MesmoAvisoDuasVezesNoTurnoSaiUmaVez()
    {
        var sut = Criar();
        var contexto = Contexto();

        await sut.ExecutarAsync(contexto, Entrada("suspeita", "Comprovante com valor diferente"));
        var segunda = await sut.ExecutarAsync(contexto, Entrada("suspeita", "De novo"));

        segunda.Should().Contain("ja_avisado");
        _notificador.ReceivedCalls().Should().HaveCount(1);
    }

    [Fact]
    public async Task JaAvisadoHojeNaoRepete()
    {
        _eventos.ExisteCorrelacaoAsync(default, default!).ReturnsForAnyArgs(true);

        var r = await Criar().ExecutarAsync(Contexto(), Entrada("reclamacao", "Atrasou"));

        r.Should().Contain("ja_avisado");
        _notificador.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("elogio", "Gostou")]
    [InlineData("reclamacao", "   ")]
    public async Task EntradaInvalidaNaoAvisa(string tipo, string resumo)
    {
        var r = await Criar().ExecutarAsync(Contexto(), Entrada(tipo, resumo));

        r.Should().Contain("erro");
        _notificador.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Ligou do 11 99999-0000 e pediu estorno", "Ligou do … e pediu estorno")]
    [InlineData("Cartão 4111 1111 1111 1111 recusado", "Cartão … recusado")]
    [InlineData("Pediu 2 lasanhas e 12 nhoques", "Pediu 2 lasanhas e 12 nhoques")]
    public void ResumoPerdeSequenciasLongasDeDigitos(string resumo, string esperado) =>
        AvisarGestaoFerramenta.SemDigitosLongos(resumo).Should().Be(esperado);
}
