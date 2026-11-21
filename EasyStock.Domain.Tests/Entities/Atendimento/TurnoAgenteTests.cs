using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

public class TurnoAgenteTests
{
    private static readonly DateTime Agora = new(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc);

    private static TurnoAgente Registrar(Guid? empresaId = null, int tokensEntrada = 100, string? modelo = "claude-sonnet-5") =>
        TurnoAgente.Registrar(empresaId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), modelo,
            tokensEntrada, 20, 5, 0.0012m, [new FerramentaDoTurno("consultar_pedido", true), new FerramentaDoTurno("criar_pedido", false)],
            2, DesfechoTurnoAgente.Respondeu, 1500, Agora);

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
    public void ModeloVazioViraDesconhecido() => Registrar(modelo: " ").Modelo.Should().Be("desconhecido");

    [Fact]
    public void RecusaEmpresaVaziaETokensNegativos()
    {
        FluentActions.Invoking(() => Registrar(empresaId: Guid.Empty)).Should().Throw<RegraDeDominioVioladaException>();
        FluentActions.Invoking(() => Registrar(tokensEntrada: -1)).Should().Throw<RegraDeDominioVioladaException>();
    }
}
