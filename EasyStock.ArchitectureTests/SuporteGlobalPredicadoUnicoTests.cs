using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard B1 (#2070): "é suporte da plataforma" tem uma regra só, <c>Perfil.EhSuporteGlobal</c> (nível SuperAdmin E
/// <c>EmpresaId</c> nulo), e as consultas do EF usam a expressão <c>Perfil.SuporteGlobal</c>. Antes, sete lugares
/// decidiam só pelo nível do perfil, e um perfil de empresa com nível SuperAdmin abria o login de suporte, as audiências
/// e o <c>api/admin/*</c>.
///
/// Source-text based (idioma de <see cref="EmailFabricaUnicaTests"/>), comentários ignorados. Proíbe comparar o
/// <c>Nivel</c> de um PERFIL com <c>NivelAcesso.SuperAdmin</c> fora de <c>Perfil.cs</c>: receptor chamado
/// <c>perfil</c>/<c>Perfil</c>, ou parâmetro de lambda de uma expressão sobre perfis. O nível do usuário logado
/// (<c>currentUser.Nivel</c>, que vem do JWT) não é perfil e segue livre. Red verificado antes da correção: o teste
/// listou os lugares do B1.
/// </summary>
[Trait("Category", "Architecture")]
public class SuporteGlobalPredicadoUnicoTests
{
    private const string LugarDoPredicado = "EasyStock.Domain/Entities/Perfil.cs";

    private static readonly string[] Projetos =
        ["EasyStock.Domain", "EasyStock.Application", "EasyStock.Infra.Postgre", "EasyStock.Api"];

    private const string SuperAdmin = @"(?:[\w.]*\.)?NivelAcesso\.SuperAdmin\b";

    /// <summary><c>x.Nivel == SuperAdmin</c>, <c>x?.Nivel != SuperAdmin</c>, <c>x.Nivel is SuperAdmin</c> e o inverso.</summary>
    private static readonly Regex ComparaNivel = new(
        @"\b(?<recv>[A-Za-z_]\w*)\s*\??\s*\.\s*Nivel\s*(?:==|!=|\bis\s+(?:not\s+)?)\s*" + SuperAdmin
        + @"|" + SuperAdmin + @"\s*(?:==|!=)\s*(?<recv>[A-Za-z_]\w*)\s*\??\s*\.\s*Nivel\b",
        RegexOptions.Compiled);

    /// <summary><c>x is { Nivel: SuperAdmin }</c> (padrão de propriedade).</summary>
    private static readonly Regex PadraoDeNivel = new(
        @"\b(?<recv>[A-Za-z_]\w*)\s*\??\s+is\s+(?:not\s+)?\{[^{}]*\bNivel\s*:\s*(?:not\s+)?" + SuperAdmin,
        RegexOptions.Compiled);

    [Fact]
    public void So_o_Perfil_compara_o_nivel_SuperAdmin_de_um_perfil()
    {
        var root = RepoPaths.FindRepoRoot();
        var violacoes = new List<string>();

        foreach (var arquivo in ArquivosDeProducao(root))
        {
            var relativo = Path.GetRelativePath(root, arquivo).Replace(Path.DirectorySeparatorChar, '/');
            if (relativo.Equals(LugarDoPredicado, StringComparison.OrdinalIgnoreCase)) continue;

            var codigo = SemComentarios(File.ReadAllText(arquivo));
            violacoes.AddRange(ComparacoesDePerfil(codigo).Select(trecho => $"{relativo}: {trecho}"));
        }

        // A lista inteira na mensagem: o FluentAssertions mostra só o primeiro item de uma coleção não vazia.
        string.Join(Environment.NewLine, violacoes).Should().BeEmpty(
            "o nível SuperAdmin de um perfil só é comparado em {0}: use Perfil.EhSuporteGlobal (memória), "
            + "Perfil.SuporteGlobal (EF), Perfil.NivelNaEmpresa ou Perfil.AtribuivelNaEmpresa", LugarDoPredicado);
    }

    [Fact]
    public void O_predicado_existe_e_o_detector_o_enxerga()
    {
        // Sem isto o guard viraria verde-vazio se o predicado mudasse de lugar ou o detector parasse de casar.
        var root = RepoPaths.FindRepoRoot();
        var caminho = Path.Combine(root, LugarDoPredicado.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(caminho).Should().BeTrue($"{LugarDoPredicado} sumiu: atualize este guard.");
        var codigo = SemComentarios(File.ReadAllText(caminho));

        codigo.Should().Contain("SuporteGlobal").And.Contain("EmpresaId == null");
        ComparacoesDePerfil("var a = perfis.Where(p => p.Nivel != NivelAcesso.SuperAdmin);").Should().ContainSingle();
        ComparacoesDePerfil("if (perfil is { Nivel: NivelAcesso.SuperAdmin }) return;").Should().ContainSingle();
        ComparacoesDePerfil("x = up.Perfil != null && up.Perfil.Nivel == NivelAcesso.SuperAdmin;").Should().ContainSingle();
        ComparacoesDePerfil("if (currentUser.Nivel != NivelAcesso.SuperAdmin && ok) return;").Should().BeEmpty();
        ComparacoesDePerfil("var s = _currentUser is { IsAuthenticated: true, Nivel: NivelAcesso.SuperAdmin };").Should().BeEmpty();
    }

    /// <summary>Trechos que comparam o nível SuperAdmin de um perfil no código (já sem comentários).</summary>
    private static List<string> ComparacoesDePerfil(string codigo)
    {
        var achados = new List<string>();
        foreach (var m in ComparaNivel.Matches(codigo).Concat(PadraoDeNivel.Matches(codigo)))
        {
            var recv = m.Groups["recv"].Value;
            if (EhPerfil(codigo, m.Index, recv))
                achados.Add(m.Value.Trim());
        }
        return achados;
    }

    /// <summary>O receptor é perfil pelo nome, ou é parâmetro de lambda numa instrução que fala de perfis.</summary>
    private static bool EhPerfil(string codigo, int posicao, string recv)
    {
        if (recv.Contains("perfil", StringComparison.OrdinalIgnoreCase)) return true;

        var inicio = codigo.LastIndexOf(';', Math.Max(0, posicao - 1)) + 1;
        var instrucao = codigo[inicio..posicao];
        return Regex.IsMatch(instrucao, @"\b" + Regex.Escape(recv) + @"\s*=>")
            && instrucao.Contains("perfi", StringComparison.OrdinalIgnoreCase);
    }

    private static string SemComentarios(string conteudo) =>
        string.Join('\n', conteudo.Split('\n').Where(linha =>
        {
            var t = linha.TrimStart();
            return !(t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*"));
        }));

    private static IEnumerable<string> ArquivosDeProducao(string root) =>
        Projetos
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "Migrations" + Path.DirectorySeparatorChar));
}
