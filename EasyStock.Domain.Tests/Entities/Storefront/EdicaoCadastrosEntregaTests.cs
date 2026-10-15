using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Edição de janela e zona pelo cadastro da loja (S45): as mesmas regras das factories valem na edição,
/// e a cobertura da zona troca de tipo sem deixar resto do tipo anterior.
/// </summary>
public class EdicaoCadastrosEntregaTests
{
    private static readonly Guid Loja = Guid.NewGuid();

    private static JanelaEntrega Janela() =>
        JanelaEntrega.Criar(Loja, 1, new TimeOnly(9, 0), new TimeOnly(12, 0), 10, "Manhã");

    [Fact]
    public void Janela_Atualizar_TrocaDadosEAlteradoEm()
    {
        var janela = Janela();
        var antes = janela.AlteradoEm;

        janela.Atualizar(3, new TimeOnly(14, 0), new TimeOnly(18, 0), 6, "  Tarde  ");

        janela.DiaDaSemana.Should().Be(3);
        janela.HoraInicio.Should().Be(new TimeOnly(14, 0));
        janela.HoraFim.Should().Be(new TimeOnly(18, 0));
        janela.CapacidadeMaxima.Should().Be(6);
        janela.Label.Should().Be("Tarde");
        janela.AlteradoEm.Should().BeOnOrAfter(antes);
    }

    [Theory]
    [InlineData(7, 9, 12, 5, "x")]
    [InlineData(1, 12, 9, 5, "x")]
    [InlineData(1, 9, 12, 0, "x")]
    [InlineData(1, 9, 12, 5, " ")]
    public void Janela_Atualizar_Invalida_LancaENaoMuda(int dia, int ini, int fim, int cap, string label)
    {
        var janela = Janela();

        var act = () => janela.Atualizar(dia, new TimeOnly(ini, 0), new TimeOnly(fim, 0), cap, label);

        act.Should().Throw<RegraDeDominioVioladaException>();
        janela.Label.Should().Be("Manhã");
        janela.CapacidadeMaxima.Should().Be(10);
    }

    [Fact]
    public void Zona_AtualizarDados_ValidaComoNaCriacao()
    {
        var zona = FreteZona.CriarPorCep(Loja, "Centro", "05500-000", "05599-999", 8m, 40);

        zona.AtualizarDados("Centro ampliado", 9.5m, 50, 2);

        zona.Label.Should().Be("Centro ampliado");
        zona.Valor.Should().Be(9.5m);
        zona.TempoEstimadoMinutos.Should().Be(50);
        zona.Ordem.Should().Be(2);

        var act = () => zona.AtualizarDados("Centro", 0m, 50, 2);
        act.Should().Throw<RegraDeDominioVioladaException>();
        zona.Valor.Should().Be(9.5m);
    }

    [Fact]
    public void Zona_DeCepParaBairros_ZeraCep()
    {
        var zona = FreteZona.CriarPorCep(Loja, "Centro", "05500000", "05599999", 8m, 40);

        zona.DefinirCoberturaPorBairros(["Butantã", "Pinheiros", "butanta"]);

        zona.TipoCobertura.Should().Be(FreteZona.TipoBairrosLista);
        zona.CepInicio.Should().BeNull();
        zona.CepFim.Should().BeNull();
        zona.CobreBairro("BUTANTA").Should().BeTrue();
        zona.CobreBairro("pinheiros").Should().BeTrue();
        zona.CobreCep("05510000").Should().BeFalse();
    }

    [Fact]
    public void Zona_DeBairrosParaCep_ZeraBairros()
    {
        var zona = FreteZona.CriarPorBairros(Loja, "Oeste", ["Butantã"], 8m, 40);

        zona.DefinirCoberturaPorCep("01000-000", "01099-999");

        zona.TipoCobertura.Should().Be(FreteZona.TipoCepRange);
        zona.BairrosJson.Should().BeNull();
        zona.CobreCep("01050000").Should().BeTrue();
        zona.CobreBairro("butanta").Should().BeFalse();
    }

    [Fact]
    public void Zona_CoberturaInvalida_LancaENaoMuda()
    {
        var zona = FreteZona.CriarPorBairros(Loja, "Oeste", ["Butantã"], 8m, 40);

        ((Action)(() => zona.DefinirCoberturaPorCep("01099999", "01000000"))).Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => zona.DefinirCoberturaPorBairros([]))).Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => zona.DefinirCoberturaPorBairros(["ok", " "]))).Should().Throw<RegraDeDominioVioladaException>();

        zona.TipoCobertura.Should().Be(FreteZona.TipoBairrosLista);
        zona.CobreBairro("butanta").Should().BeTrue();
    }
}
