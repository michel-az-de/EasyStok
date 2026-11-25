using EasyStock.Application.Services.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#3291 (R3): o prazo do turno nasce em 180 s, lê a chave de configuração e zero ou negativo desliga.</summary>
public class PrazoDoTurnoAgenteOptionsTests
{
    private static PrazoDoTurnoAgenteOptions Ler(params (string Chave, string Valor)[] pares)
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(pares.Select(p => new KeyValuePair<string, string?>(p.Chave, p.Valor))).Build();
        var opcoes = new PrazoDoTurnoAgenteOptions();
        opcoes.Ler(configuracao);
        return opcoes;
    }

    [Fact]
    public void SemAChaveValeOPadraoDe180Segundos() =>
        Ler().Prazo.Should().Be(TimeSpan.FromSeconds(180));

    [Fact]
    public void ALeituraUsaAChaveDoServicoDaFila() =>
        Ler((PrazoDoTurnoAgenteOptions.Chave, "45")).Prazo.Should().Be(TimeSpan.FromSeconds(45));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ZeroOuNegativoDesligaOPrazo(string valor) =>
        Ler((PrazoDoTurnoAgenteOptions.Chave, valor)).Prazo.Should().BeNull();

    [Fact]
    public void ValorQueNaoEhNumeroMantemOPadrao() =>
        Ler((PrazoDoTurnoAgenteOptions.Chave, "tres minutos")).Prazo.Should().Be(TimeSpan.FromSeconds(180));

    [Fact]
    public void ValorAbsurdoVaiAoTetoDeUmDiaEntaoCancelAfterNuncaRecusa()
    {
        var prazo = Ler((PrazoDoTurnoAgenteOptions.Chave, int.MaxValue.ToString())).Prazo;

        prazo.Should().Be(TimeSpan.FromDays(1));
        using var origem = new CancellationTokenSource();
        var cancelar = () => origem.CancelAfter(prazo!.Value);
        cancelar.Should().NotThrow();
    }
}
