using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Ia;

/// <summary>
/// #3304 (R8 da revisão do harness): o teto de saída vive no modelo e o prazo no provedor, ambos opcionais (nulo usa o
/// global do .env). O domínio recusa o que o <c>CHECK</c> do banco recusaria, com os mesmos limites.
/// </summary>
public class SaidaMaxEPrazoIaTests
{
    private static readonly DateTime Agora = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Depois = Agora.AddMinutes(5);

    private static ModeloIa Modelo() =>
        ModeloIa.Criar(Guid.NewGuid(), Guid.NewGuid(), "deepseek-flash", "Flash", 0.30m, 1.20m, 0.006m, 0.5m, Agora);

    private static ProvedorIa Provedor() =>
        ProvedorIa.Criar(Guid.NewGuid(), "DeepSeek", "https://api.deepseek.com/anthropic", AutenticacaoProvedorIa.XApiKey, 20m, Agora);

    [Fact]
    public void ModeloEProvedorNascemSemTetoNemPrazoProprios()
    {
        Modelo().SaidaMax.Should().BeNull();
        Provedor().PrazoSegundos.Should().BeNull();
    }

    [Theory]
    [InlineData(ModeloIa.SaidaMaxMinima)]
    [InlineData(8192)]
    [InlineData(ModeloIa.SaidaMaxMaxima)]
    public void SaidaMaxDentroDoLimiteEhGuardada(int valor)
    {
        var modelo = Modelo();

        modelo.DefinirSaidaMax(valor, Depois);

        modelo.SaidaMax.Should().Be(valor);
        modelo.AlteradoEm.Should().Be(Depois);
    }

    [Theory]
    [InlineData(ModeloIa.SaidaMaxMinima - 1)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ModeloIa.SaidaMaxMaxima + 1)]
    public void SaidaMaxForaDoLimiteEhRecusadaESemTocarNoModelo(int valor)
    {
        var modelo = Modelo();
        modelo.DefinirSaidaMax(4096, Agora);

        var act = () => modelo.DefinirSaidaMax(valor, Depois);

        act.Should().Throw<RegraDeDominioVioladaException>();
        modelo.SaidaMax.Should().Be(4096);
        modelo.AlteradoEm.Should().Be(Agora);
    }

    [Fact]
    public void SaidaMaxNulaVoltaAoGlobal()
    {
        var modelo = Modelo();
        modelo.DefinirSaidaMax(4096, Agora);

        modelo.DefinirSaidaMax(null, Depois);

        modelo.SaidaMax.Should().BeNull();
    }

    [Theory]
    [InlineData(ProvedorIa.PrazoMinimoSegundos)]
    [InlineData(90)]
    [InlineData(ProvedorIa.PrazoMaximoSegundos)]
    public void PrazoDentroDoLimiteEhGuardado(int segundos)
    {
        var provedor = Provedor();

        provedor.DefinirPrazo(segundos, Depois);

        provedor.PrazoSegundos.Should().Be(segundos);
        provedor.AlteradoEm.Should().Be(Depois);
    }

    [Theory]
    [InlineData(ProvedorIa.PrazoMinimoSegundos - 1)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ProvedorIa.PrazoMaximoSegundos + 1)]
    public void PrazoForaDoLimiteEhRecusadoESemTocarNoProvedor(int segundos)
    {
        var provedor = Provedor();
        provedor.DefinirPrazo(60, Agora);

        var act = () => provedor.DefinirPrazo(segundos, Depois);

        act.Should().Throw<RegraDeDominioVioladaException>();
        provedor.PrazoSegundos.Should().Be(60);
        provedor.AlteradoEm.Should().Be(Agora);
    }

    [Fact]
    public void PrazoNuloVoltaAoGlobal()
    {
        var provedor = Provedor();
        provedor.DefinirPrazo(60, Agora);

        provedor.DefinirPrazo(null, Depois);

        provedor.PrazoSegundos.Should().BeNull();
    }
}
