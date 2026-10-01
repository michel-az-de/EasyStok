using System.Security.Cryptography;
using EasyStock.Api.Startup;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Startup;

/// <summary>
/// F16 (#1246): a chave-mestra (KEK) que cifra as credenciais das integrações. Em Production a API
/// não sobe sem ela e diz qual variável falta; fora de Production, ausente é aceito, mas se
/// configurada tem de ser válida. Toda KEK aqui é gerada no próprio teste.
/// </summary>
public class ChaveMestraTests
{
    private static string KekNova() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static Func<string, string?> Ambiente(params (string Nome, string Valor)[] vars)
    {
        var mapa = vars.ToDictionary(v => v.Nome, v => v.Valor);
        return nome => mapa.TryGetValue(nome, out var valor) ? valor : null;
    }

    [Fact]
    public void SemKekEmProducaoNaoSobeEDizOMotivo()
    {
        var act = () => StartupHardening.ValidateChaveMestraCore(isProduction: true, currentKekId: null, _ => null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EZ_CRYPTO_KEK_ID*EZ_CRYPTO_KEK*");
    }

    [Fact]
    public void KekIdSemValorEmProducaoNaoSobe()
    {
        var act = () => StartupHardening.ValidateChaveMestraCore(true, "kek-2026-09", _ => null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*kek-2026-09*EZ_CRYPTO_KEK*");
    }

    [Theory]
    [InlineData("nao-e-base64!!")]
    [InlineData("AAAA")] // 3 bytes
    public void KekInvalidaFalhaEmQualquerAmbiente(string valor)
    {
        var producao = () => StartupHardening.ValidateChaveMestraCore(true, "k1", _ => valor);
        var dev = () => StartupHardening.ValidateChaveMestraCore(false, "k1", _ => valor);

        producao.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
        dev.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }

    [Fact]
    public void SemKekForaDeProducaoSobe()
    {
        var act = () => StartupHardening.ValidateChaveMestraCore(false, null, _ => null);

        act.Should().NotThrow("dev e testes sobem sem KEK; o PUT de credencial é que responde 503");
    }

    [Fact]
    public void KekValidaEmProducaoSobe()
    {
        var kek = KekNova();
        var act = () => StartupHardening.ValidateChaveMestraCore(true, "k1", id => id == "k1" ? kek : null);

        act.Should().NotThrow();
    }

    [Fact]
    public void VariaveisEzViramCrypto()
    {
        var atual = KekNova();
        var anterior = KekNova();

        var config = ChaveMestraConfiguracao.MapearVariaveisEz(Ambiente(
            ("EZ_CRYPTO_KEK_ID", "kek-2026-09"), ("EZ_CRYPTO_KEK", atual),
            ("EZ_CRYPTO_KEK_ANTERIOR_ID", "kek-2026-01"), ("EZ_CRYPTO_KEK_ANTERIOR", anterior)));

        config.Should().Contain(new KeyValuePair<string, string?>("Crypto:CurrentKekId", "kek-2026-09"));
        config.Should().Contain(new KeyValuePair<string, string?>("Crypto:Keks:kek-2026-09", atual));
        config.Should().Contain(new KeyValuePair<string, string?>("Crypto:Keks:kek-2026-01", anterior));
    }

    [Fact]
    public void SemVariaveisEzNadaEhMapeado()
    {
        ChaveMestraConfiguracao.MapearVariaveisEz(Ambiente()).Should().BeEmpty(
            "Crypto:* configurado direto (appsettings, Crypto__*) continua valendo");
    }
}
