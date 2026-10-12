using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

/// <summary>
/// Permissão efetiva (S41): claims explícitas mandam; sem elas, vale o nível. O servidor usa a mesma
/// regra para o usuário logado e para o destino de uma transferência de conversa.
/// </summary>
public class PoliticaPermissaoTests
{
    [Fact]
    public void ListaExplicitamenteVazia_NegaTudoEmTodosOsNiveis()
    {
        foreach (var nivel in Enum.GetValues<NivelAcesso>())
        foreach (var permissao in Enum.GetValues<Permissao>())
            PoliticaPermissao.Tem(nivel, [], permissao, permissoesExplicitas: true).Should().BeFalse();
    }

    [Theory]
    [InlineData(999)]
    public void ValorNumericoInvalido_NuncaConcedeAcesso(int valor) =>
        PoliticaPermissao.Tem(NivelAcesso.SuperAdmin, [(Permissao)valor], (Permissao)valor).Should().BeFalse();

    [Theory]
    [InlineData(NivelAcesso.SuperAdmin)]
    [InlineData(NivelAcesso.Admin)]
    [InlineData(NivelAcesso.Gerente)]
    [InlineData(NivelAcesso.Operador)]
    public void AtenderConversas_PeloNivel_OperadorParaCima(NivelAcesso nivel) =>
        PoliticaPermissao.Tem(nivel, [], Permissao.AtenderConversas).Should().BeTrue();

    [Fact]
    public void AtenderConversas_Visualizador_NaoTem() =>
        PoliticaPermissao.Tem(NivelAcesso.Visualizador, [], Permissao.AtenderConversas).Should().BeFalse();

    [Fact]
    public void ClaimsExplicitas_SemAtenderConversas_NaoTemMesmoComNivelAlto() =>
        PoliticaPermissao.Tem(NivelAcesso.Admin, [Permissao.GerenciarProdutos], Permissao.AtenderConversas)
            .Should().BeFalse();

    [Fact]
    public void ClaimsExplicitas_ComAtenderConversas_Tem() =>
        PoliticaPermissao.Tem(NivelAcesso.Operador, [Permissao.AtenderConversas], Permissao.AtenderConversas)
            .Should().BeTrue();

    [Fact]
    public void FallbackPorNivel_PreservaRegrasAnteriores()
    {
        PoliticaPermissao.Tem(NivelAcesso.Admin, [], Permissao.ConfigurarSla).Should().BeFalse();
        PoliticaPermissao.Tem(NivelAcesso.Gerente, [], Permissao.GerenciarUsuarios).Should().BeFalse();
        PoliticaPermissao.Tem(NivelAcesso.Operador, [], Permissao.ResponderTickets).Should().BeTrue();
        PoliticaPermissao.Tem(NivelAcesso.Operador, [], Permissao.GerenciarUsuarios).Should().BeFalse();
        PoliticaPermissao.Tem(NivelAcesso.Visualizador, [], Permissao.VisualizarRelatorios).Should().BeTrue();
    }
}
