using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

public class TurnoAgenteTests
{
    private static readonly DateTime Agora = new(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc);

    private static TurnoAgente Registrar(Guid? empresaId = null, int tokensEntrada = 100, string? modelo = "claude-sonnet-5", string? motivo = null) =>
        TurnoAgente.Registrar(empresaId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), modelo,
            tokensEntrada, 20, 5, 0.0012m, [new FerramentaDoTurno("consultar_pedido", true), new FerramentaDoTurno("criar_pedido", false)],
            2, DesfechoTurnoAgente.Respondeu, 1500, Agora, motivo);

    [Fact]
    public void GuardaNumerosFerramentasEDesfecho()
    {
        var turno = Registrar();

        turno.TotalTokens.Should().Be(125);
        turno.Ferramentas.Should().Equal(new FerramentaDoTurno("consultar_pedido", true), new FerramentaDoTurno("criar_pedido", false));
        turno.FerramentasJson.Should().Be("""[{"nome":"consultar_pedido","ok":true},{"nome":"criar_pedido","ok":false}]""");
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Respondeu);
        turno.CriadoEm.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void NaoTemTextoNemTelefone()
    {
        // O que o cliente disse fica só em atendimento_mensagens; o registro do turno guarda ids e números.
        typeof(TurnoAgente).GetProperties().Select(p => p.Name)
            .Should().NotContain(nome => nome.Contains("Texto") || nome.Contains("Telefone") || nome.Contains("Conteudo"));
    }

    [Fact]
    public void IdInformadoVaiParaOTurnoEIdVazioNaoPassa()
    {
        // #3287 (R5): o id nasce antes das chamadas de IA e é o que o turno guarda.
        var id = Guid.NewGuid();
        var turno = TurnoAgente.Registrar(Guid.NewGuid(), Guid.NewGuid(), null, null, "m", 1, 1, 0, null, [], 1,
            DesfechoTurnoAgente.Respondeu, 1, Agora, id: id);
        turno.Id.Should().Be(id);

        var vazio = () => TurnoAgente.Registrar(Guid.NewGuid(), Guid.NewGuid(), null, null, "m", 1, 1, 0, null, [], 1,
            DesfechoTurnoAgente.Respondeu, 1, Agora, id: Guid.Empty);
        vazio.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ModeloVazioViraDesconhecido() => Registrar(modelo: " ").Modelo.Should().Be("desconhecido");

    [Fact]
    public void RecusaEmpresaVaziaETokensNegativos()
    {
        FluentActions.Invoking(() => Registrar(empresaId: Guid.Empty)).Should().Throw<RegraDeDominioVioladaException>();
        FluentActions.Invoking(() => Registrar(tokensEntrada: -1)).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("portao:limite")]
    [InlineData("guarda:valor")]
    [InlineData("falha_llm")]
    [InlineData("escalada_modelo")]
    public void GuardaOMotivoEmCodigoValido(string motivo) => Registrar(motivo: motivo).Motivo.Should().Be(motivo);

    [Fact]
    public void RespostaNormalGravaMotivoNulo() => Registrar().Motivo.Should().BeNull();

    [Fact]
    public void AceitaMotivoDeAte48Caracteres() => Registrar(motivo: new string('a', 48)).Motivo.Should().HaveLength(48);

    [Theory]
    [InlineData("Portao:limite")]
    [InlineData("portao limite")]
    [InlineData("portão:limite")]
    [InlineData("guarda:valor2")]
    [InlineData("")]
    [InlineData("R$ 59,00")]
    public void RecusaMotivoForaDoPadrao(string motivo) =>
        FluentActions.Invoking(() => Registrar(motivo: motivo)).Should().Throw<RegraDeDominioVioladaException>();

    [Fact]
    public void RecusaMotivoComMaisDe48Caracteres() =>
        FluentActions.Invoking(() => Registrar(motivo: new string('a', 49))).Should().Throw<RegraDeDominioVioladaException>();
}
