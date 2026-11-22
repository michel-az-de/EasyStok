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
/// <c>Nivel</c> de um PERFIL com <c>NivelAcesso.SuperAdmin</c> fora de <c>Perfil.cs</c>, em qualquer forma: <c>==</c>,
/// <c>!=</c>, <c>is</c> (com <c>or</c>/<c>not</c>), padrão de propriedade, <c>switch</c> com <c>case</c>, expressão
/// <c>switch</c> e o nível guardado antes numa variável. É perfil o receptor chamado <c>perfil</c>/<c>Perfil</c>, o
/// declarado com o tipo <c>Perfil</c>, o que vem de uma expressão sobre perfis (<c>var</c>, <c>foreach</c>) e o parâmetro
/// de lambda numa instrução sobre perfis. O nível do usuário logado (<c>currentUser.Nivel</c>, que vem do JWT) não é
/// perfil e segue livre. É heurístico, não semântico (o projeto não tem Roslyn): cada forma tem a sua amostra abaixo.
/// Red verificado antes da correção: o teste listou os lugares do B1.
/// </summary>
[Trait("Category", "Architecture")]
public class SuporteGlobalPredicadoUnicoTests
{
    private const string LugarDoPredicado = "EasyStock.Domain/Entities/Perfil.cs";

    private static readonly string[] Projetos =
        ["EasyStock.Domain", "EasyStock.Application", "EasyStock.Infra.Postgre", "EasyStock.Api"];

    private const string SuperAdmin = @"(?:[\w.]*\.)?NivelAcesso\.SuperAdmin\b";

    /// <summary>O <c>Nivel</c> de um receptor: <c>x.Nivel</c>, <c>x?.Nivel</c>, <c>x!.Nivel</c>.</summary>
    private const string NivelDe = @"\b(?<recv>[A-Za-z_]\w*)\s*[!?]?\s*\.\s*Nivel\b";

    /// <summary>
    /// Depois de <c>is</c>, até o SuperAdmin, sem sair da expressão: pega <c>is not X</c> e <c>is A or X</c>, mas não um
    /// <c>&amp;&amp;</c> ou <c>||</c> que comece outra comparação.
    /// </summary>
    private const string PadraoIs = @"\s+is\s+[^;{}()&|]*?" + SuperAdmin;

    /// <summary>Um <c>case</c> com o SuperAdmin dentro do bloco do <c>switch</c>, e o braço de uma expressão <c>switch</c>.</summary>
    private const string CaseSuperAdmin = @"\s*\)\s*\{[^}]*?\bcase\s+[^:;]*?" + SuperAdmin;
    private const string BracoSuperAdmin = @"\s+switch\s*\{[^}]*?" + SuperAdmin;

    /// <summary><c>x.Nivel == SuperAdmin</c>, <c>!=</c>, <c>is</c> (com <c>not</c>/<c>or</c>), o inverso e os dois <c>switch</c>.</summary>
    private static readonly Regex ComparaNivel = new(
        NivelDe + @"\s*(?:==|!=)\s*" + SuperAdmin
        + @"|" + NivelDe + PadraoIs
        + @"|" + SuperAdmin + @"\s*(?:==|!=)\s*" + NivelDe
        + @"|\bswitch\s*\(\s*" + NivelDe + CaseSuperAdmin
        + @"|" + NivelDe + BracoSuperAdmin,
        RegexOptions.Compiled);

    /// <summary><c>x is { Nivel: SuperAdmin }</c> (padrão de propriedade).</summary>
    private static readonly Regex PadraoDeNivel = new(
        @"\b(?<recv>[A-Za-z_]\w*)\s*\??\s+is\s+(?:not\s+)?\{[^{}]*\bNivel\s*:\s*(?:not\s+)?" + SuperAdmin,
        RegexOptions.Compiled);

    /// <summary>O nível guardado numa variável antes da comparação: <c>var n = perfil.Nivel;</c>.</summary>
    private static readonly Regex NivelGuardado = new(
        @"\b(?:var|NivelAcesso\??)\s+(?<alias>[A-Za-z_]\w*)\s*=\s*(?:[A-Za-z_]\w*\s*[!?]?\s*\.\s*)*" + NivelDe + @"\s*;",
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

    /// <summary>As formas que o detector de texto deixava passar (revisão do #2070): cada uma tem de ser acusada.</summary>
    [Theory]
    [InlineData("if (perfil.Nivel is NivelAcesso.Admin or NivelAcesso.SuperAdmin) return;")]
    [InlineData("if (perfil?.Nivel is not NivelAcesso.SuperAdmin) return;")]
    [InlineData("switch (perfil.Nivel) { case NivelAcesso.Admin: break; case NivelAcesso.SuperAdmin: return; }")]
    [InlineData("var x = perfil.Nivel switch { NivelAcesso.SuperAdmin => 1, _ => 0 };")]
    [InlineData("var n = perfil.Nivel; if (n == NivelAcesso.SuperAdmin) return;")]
    [InlineData("NivelAcesso nivel = up.Perfil!.Nivel; return nivel is NivelAcesso.SuperAdmin;")]
    [InlineData("if (up.Perfil!.Nivel == NivelAcesso.SuperAdmin) return;")]
    [InlineData("Perfil? alvo = await repo.GetByIdAsync(id); if (alvo.Nivel == NivelAcesso.SuperAdmin) return;")]
    [InlineData("foreach (var x in usuario.Perfis) { if (x.Nivel != NivelAcesso.SuperAdmin) continue; }")]
    [InlineData("var p = await perfilRepository.GetByIdAsync(id); if (p.Nivel == NivelAcesso.SuperAdmin) return;")]
    public void O_detector_acusa_as_outras_formas_de_comparar(string codigo) =>
        ComparacoesDePerfil(codigo).Should().ContainSingle(codigo);

    /// <summary>O nível do usuário logado (do JWT) não é perfil: nenhuma das formas acima acusa sobre ele.</summary>
    [Theory]
    [InlineData("var nivel = currentUser.Nivel; if (nivel == NivelAcesso.SuperAdmin) return;")]
    [InlineData("switch (currentUser.Nivel) { case NivelAcesso.SuperAdmin: break; }")]
    [InlineData("var x = atual.Nivel switch { NivelAcesso.SuperAdmin => 1, _ => 0 };")]
    [InlineData("if (currentUser.Nivel is NivelAcesso.Admin or NivelAcesso.SuperAdmin) return;")]
    [InlineData("var usuario = await usuarios.GetByIdAsync(id); if (usuario.Nivel == NivelAcesso.SuperAdmin) return;")]
    public void O_detector_nao_acusa_o_nivel_do_usuario_logado(string codigo) =>
        ComparacoesDePerfil(codigo).Should().BeEmpty(codigo);

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

        foreach (Match guardado in NivelGuardado.Matches(codigo))
        {
            if (!EhPerfil(codigo, guardado.Index, guardado.Groups["recv"].Value)) continue;
            var alias = Regex.Escape(guardado.Groups["alias"].Value);
            var depois = codigo[(guardado.Index + guardado.Length)..];
            var comparacao = Regex.Match(depois,
                @"\b" + alias + @"\s*(?:==|!=)\s*" + SuperAdmin
                + @"|\b" + alias + PadraoIs
                + @"|" + SuperAdmin + @"\s*(?:==|!=)\s*" + alias + @"\b"
                + @"|\bswitch\s*\(\s*" + alias + CaseSuperAdmin
                + @"|\b" + alias + BracoSuperAdmin);
            if (comparacao.Success)
                achados.Add($"{guardado.Value.Trim()} ... {comparacao.Value.Trim()}");
        }
        return achados;
    }

    /// <summary>
    /// O receptor é perfil: pelo nome, pelo tipo declarado (<c>Perfil x</c>), por vir de uma expressão sobre perfis
    /// (<c>var x = ...perfil...</c>, <c>foreach (var x in ...Perfis)</c>) ou por ser parâmetro de lambda numa instrução que
    /// fala de perfis.
    /// </summary>
    private static bool EhPerfil(string codigo, int posicao, string recv)
    {
        if (recv.Contains("perfil", StringComparison.OrdinalIgnoreCase)) return true;

        var antes = codigo[..posicao];
        var nome = Regex.Escape(recv);
        if (Regex.IsMatch(antes, @"\bPerfil\??\s+" + nome + @"\b")) return true;
        var declaracao = Regex.Matches(antes, @"\b(?:var\s+" + nome + @"\s*=|foreach\s*\(\s*var\s+" + nome + @"\s+in\b)(?<origem>[^;)]*)");
        if (declaracao.Count > 0
            && declaracao[^1].Groups["origem"].Value.Contains("perfi", StringComparison.OrdinalIgnoreCase)) return true;

        var inicio = codigo.LastIndexOf(';', Math.Max(0, posicao - 1)) + 1;
        var instrucao = codigo[inicio..posicao];
        return Regex.IsMatch(instrucao, @"\b" + nome + @"\s*=>")
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
