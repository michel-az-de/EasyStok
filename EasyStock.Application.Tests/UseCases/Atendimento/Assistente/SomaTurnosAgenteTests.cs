using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Assistente;

/// <summary>
/// #3370 (D2 da #3271): o piso das chamadas sobre o consumo dos turnos. Vence o registro com mais tokens; nunca a soma
/// dos dois (contaria duas vezes) e nunca abaixo do que os turnos já mostram.
/// </summary>
public class SomaTurnosAgenteTests
{
    // 5 turnos que somam 1.000 de entrada, 200 de saída e 500 de cache lido (1.700 tokens): 2 sem IA e 3 com token.
    private static SomaTurnosAgente DosTurnos() =>
        new(5, 1_000, 200, 500, 0.012m, 1, RespostasSemIa: 2, TurnosComTokens: 3);

    [Fact]
    public void ChamadasComMaisTokens_ViramOConsumo_EOResto_FicaDosTurnos()
    {
        var soma = DosTurnos().ComPisoDasChamadas(1_500, 300, 600);

        soma.TokensEntrada.Should().Be(1_500);
        soma.TokensSaida.Should().Be(300);
        soma.TokensCacheLido.Should().Be(600);
        soma.TotalTokens.Should().Be(2_400);
        soma.TokensDosTurnos.Should().Be(1_700);
        soma.Turnos.Should().Be(5);
        soma.Custo.Should().Be(0.012m);
        soma.TurnosSemPreco.Should().Be(1);
        soma.RespostasSemIa.Should().Be(2);
        soma.TurnosComTokens.Should().Be(3);
    }

    [Fact]
    public void ChamadasComMenosTokens_NaoBaixamOConsumo()
    {
        var soma = DosTurnos().ComPisoDasChamadas(900, 100, 400);

        soma.Should().Be(DosTurnos());
        soma.TokensDosTurnos.Should().BeNull("o consumo continua sendo o dos turnos");
    }

    [Fact]
    public void Empate_FicaNoTurno_MesmoComOsCamposDistribuidosDeOutroJeito()
    {
        // Mesmo total (1.700) e campos diferentes: quem decide o empate decide os campos que o cartão mostra.
        var soma = DosTurnos().ComPisoDasChamadas(200, 500, 1_000);

        soma.Should().Be(DosTurnos());
        soma.TokensEntrada.Should().Be(1_000);
        soma.TokensSaida.Should().Be(200);
        soma.TokensCacheLido.Should().Be(500);
    }

    [Fact]
    public void ChamadasIguaisAosTurnos_NaoContamDuasVezes()
    {
        var soma = DosTurnos().ComPisoDasChamadas(1_000, 200, 500);

        soma.TotalTokens.Should().Be(1_700);
        soma.Should().Be(DosTurnos());
    }

    [Fact]
    public void SemTurnoNenhum_AsChamadasSaoOConsumo()
    {
        var soma = SomaTurnosAgente.Vazia.ComPisoDasChamadas(10, 20, 30);

        soma.TotalTokens.Should().Be(60);
        soma.TokensDosTurnos.Should().Be(0);
        soma.Turnos.Should().Be(0);
    }

    [Fact]
    public void ChamadasZeradas_NaoMudamNada()
    {
        SomaTurnosAgente.Vazia.ComPisoDasChamadas(0, 0, 0).Should().Be(SomaTurnosAgente.Vazia);
        DosTurnos().ComPisoDasChamadas(0, 0, 0).Should().Be(DosTurnos());
    }

    [Fact]
    public void AEstimativaDePoupados_SegueComOsTokensDosTurnos_NaoComOPiso()
    {
        // 2 respostas sem IA e 3 turnos com token: 1.700 tokens em 3 dão a média, e 2 sem IA poupam 1.133. O piso (2.400)
        // traz tokens de turno que nem foi gravado: se entrasse na média, a estimativa subiria sem nenhum turno a mais.
        DosTurnos().TokensPoupadosEstimados.Should().Be(1_133);

        var comPiso = DosTurnos().ComPisoDasChamadas(1_500, 300, 600);

        comPiso.TokensPoupadosEstimados.Should().Be(1_133);
    }
}
