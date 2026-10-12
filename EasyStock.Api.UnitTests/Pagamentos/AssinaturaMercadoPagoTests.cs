using EasyStock.Infra.Async.Pagamentos.Webhooks;
using EasyStock.TestHelpers.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// #1634: a fábrica <see cref="AssinaturaMercadoPago"/> só serve se o validador REAL aceitar o que ela gera
/// e recusar qualquer adulteração. Sem isto, um teste de webhook poderia passar com assinatura que a produção rejeita.
/// </summary>
public class AssinaturaMercadoPagoTests
{
    private const string Segredo = "whsec_test_mp_secret_long_enough_chars";
    private const string DataId = "20359978";
    private const string RequestId = "req-1634";

    private static MercadoPagoSignatureValidator Validador(string segredo = Segredo)
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MercadoPago:WebhookSecret"] = segredo })
            .Build();
        var ambiente = Substitute.For<IHostEnvironment>();
        ambiente.EnvironmentName.Returns("Production");
        return new MercadoPagoSignatureValidator(configuracao, NullLogger<MercadoPagoSignatureValidator>.Instance, ambiente);
    }

    private static string Corpo(string dataId = DataId) => "{\"type\":\"payment\",\"data\":{\"id\":\"" + dataId + "\"}}";

    [Fact]
    public void AssinaturaGerada_EAceitaPeloValidadorReal()
    {
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(Segredo, DataId, RequestId, DateTimeOffset.UtcNow);

        Validador().Validar(Corpo(), cabecalhos).Should().BeTrue();
    }

    [Fact]
    public void DataIdNumericoNoCorpo_TambemEAceito()
    {
        // O Mercado Pago manda data.id ora como texto, ora como número; o manifest usa o mesmo valor.
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(Segredo, DataId, RequestId, DateTimeOffset.UtcNow);

        Validador().Validar("{\"data\":{\"id\":" + DataId + "}}", cabecalhos).Should().BeTrue();
    }

    [Fact]
    public void Gerar_TemOFormatoTsEV1()
    {
        var instante = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        var assinatura = AssinaturaMercadoPago.Gerar(Segredo, DataId, RequestId, instante);

        assinatura.Should().MatchRegex($"^ts={instante.ToUnixTimeMilliseconds()},v1=[0-9a-f]{{64}}$");
    }

    [Fact]
    public void DataIdAdulterado_ERecusado()
    {
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(Segredo, DataId, RequestId, DateTimeOffset.UtcNow);

        Validador().Validar(Corpo("20359979"), cabecalhos).Should().BeFalse();
    }

    [Fact]
    public void SegredoAdulterado_ERecusado()
    {
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(Segredo + "x", DataId, RequestId, DateTimeOffset.UtcNow);

        Validador().Validar(Corpo(), cabecalhos).Should().BeFalse();
    }

    [Fact]
    public void RequestIdAdulterado_ERecusado()
    {
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(Segredo, DataId, RequestId, DateTimeOffset.UtcNow);
        cabecalhos[AssinaturaMercadoPago.HeaderRequestId] = RequestId + "x";

        Validador().Validar(Corpo(), cabecalhos).Should().BeFalse();
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    public void TsForaDaJanelaDeCincoMinutos_ERecusado(int minutos)
    {
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(
            Segredo, DataId, RequestId, DateTimeOffset.UtcNow.AddMinutes(minutos));

        Validador().Validar(Corpo(), cabecalhos).Should().BeFalse();
    }

    [Fact]
    public void TsDentroDaJanela_EAceito()
    {
        var cabecalhos = AssinaturaMercadoPago.Cabecalhos(
            Segredo, DataId, RequestId, DateTimeOffset.UtcNow.AddMinutes(-4));

        Validador().Validar(Corpo(), cabecalhos).Should().BeTrue();
    }
}
