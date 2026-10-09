using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

public class AcessoModulosTests
{
    public static IEnumerable<object[]> MatrizFallback()
    {
        var esperados = new Dictionary<NivelAcesso, Modulo[]>
        {
            [NivelAcesso.SuperAdmin] = Enum.GetValues<Modulo>(),
            [NivelAcesso.Admin] = Enum.GetValues<Modulo>(),
            [NivelAcesso.Gerente] = [Modulo.Cardapio, Modulo.Producao, Modulo.Atendimento, Modulo.Cozinha,
                Modulo.Caixa, Modulo.Campanhas, Modulo.Entregas],
            [NivelAcesso.Operador] = [Modulo.Atendimento, Modulo.Cozinha, Modulo.Caixa, Modulo.Entregas],
            [NivelAcesso.Visualizador] = []
        };
        foreach (var (nivel, liberados) in esperados)
            foreach (var modulo in Enum.GetValues<Modulo>()) yield return [nivel, modulo, liberados.Contains(modulo)];
    }

    [Theory]
    [MemberData(nameof(MatrizFallback))]
    public void SemListaExplicita_AplicaMatriz(NivelAcesso nivel, Modulo modulo, bool esperado) =>
        PoliticaPermissao.Tem(nivel, [], AcessoModulos.PermissaoDe(modulo)).Should().Be(esperado);

    [Theory]
    [InlineData("Dona", "cardapio,producao,atendimento,cozinha,financeiro,campanhas,configuracoes,entregas", null)]
    [InlineData("Atendimento", "cardapio,atendimento,cozinha,financeiro,entregas", "atendimento")]
    [InlineData("Cozinha", "producao,cozinha", "cozinha")]
    public void PerfisIniciais_TemSomenteOsModulosDecididos(string nome, string ids, string? entrada)
    {
        var perfil = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == nome);
        Enum.GetValues<Modulo>().Where(m => PoliticaPermissao.Tem(perfil.Nivel, perfil.Permissoes, AcessoModulos.PermissaoDe(m)))
            .Select(AcessoModulos.IdDe).Should().BeEquivalentTo(ids.Split(','));
        perfil.ModuloInicial.Should().Be(entrada);
    }

    [Fact]
    public void ListaExplicita_NaoHerdaOsModulosDoAdmin()
    {
        foreach (var modulo in Enum.GetValues<Modulo>())
            PoliticaPermissao.Tem(NivelAcesso.Admin, [Permissao.GerenciarEstoque], AcessoModulos.PermissaoDe(modulo))
                .Should().BeFalse();
    }

    [Fact]
    public void PermissoesFinas_NaoSePerdemNosPerfisNovos()
    {
        var cozinha = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == "Cozinha");
        PoliticaPermissao.Tem(cozinha.Nivel, cozinha.Permissoes, Permissao.GerenciarEstoque).Should().BeTrue();
        PoliticaPermissao.Tem(cozinha.Nivel, cozinha.Permissoes, Permissao.AtenderConversas).Should().BeFalse();
        PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == "Dona").Permissoes.Should().HaveCount(19);
    }
}
