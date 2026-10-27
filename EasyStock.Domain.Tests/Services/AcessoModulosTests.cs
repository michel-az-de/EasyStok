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
    [InlineData("Administração", "cardapio,producao,atendimento,cozinha,financeiro,campanhas,configuracoes,entregas", null)]
    [InlineData("Gerência", "cardapio,producao,atendimento,cozinha,financeiro,campanhas,entregas", null)]
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
        PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == "Administração").Permissoes.Should().HaveCount(19);
        var gerencia = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == "Gerência");
        gerencia.Nivel.Should().Be(NivelAcesso.Gerente);
        foreach (var fina in new[] { Permissao.AtenderConversas, Permissao.GerenciarProdutos, Permissao.GerenciarEstoque, Permissao.VisualizarRelatorios })
            PoliticaPermissao.Tem(gerencia.Nivel, gerencia.Permissoes, fina, permissoesExplicitas: true).Should().BeTrue(fina.ToString());
        PoliticaPermissao.Tem(gerencia.Nivel, gerencia.Permissoes, Permissao.GerenciarUsuarios, permissoesExplicitas: true).Should().BeFalse();
    }

    [Fact]
    public void Dona_nao_e_mais_nome_de_perfil_so_a_marca_do_vinculo() =>
        // #2070: "Dona" na tela vem de UsuarioEmpresa.EhDona; o perfil de nível Admin da casa se chama Administração.
        PerfisCasaDaBaba.Iniciais.Select(p => p.Nome).Should().Equal("Administração", "Gerência", "Atendimento", "Cozinha");
}
