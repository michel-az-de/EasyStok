using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (I4.1, #1674): a chave-mestra do cofre chega à API e ao Worker pela mesma extensão. O Worker decifra o
/// token do WhatsApp; se só a API mapear <c>EZ_CRYPTO_*</c>, o mesmo <c>.env</c> faz um host ler a credencial e o
/// outro não, e nenhum teste de unidade enxerga a linha que falta no <c>Program.cs</c>.
///
/// Source-text based (idioma de <see cref="EmailFabricaUnicaTests"/>), comentários de linha ignorados.
/// </summary>
[Trait("Category", "Architecture")]
public class ChaveMestraNosDoisHostsTests
{
    private static readonly Regex ComentarioDeLinha = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Theory]
    [InlineData("EasyStock.Api/Program.cs")]
    [InlineData("EasyStock.Worker/Program.cs")]
    public void Host_mapeia_a_chave_mestra_e_avisa_se_faltar(string program)
    {
        var codigo = ComentarioDeLinha.Replace(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), program)), string.Empty);

        codigo.Should().MatchRegex(@"\.Configuration\.AdicionarChaveMestraDoAmbiente\s*\(",
            "sem isto o host nao enxerga EZ_CRYPTO_KEK_ID e EZ_CRYPTO_KEK");
        codigo.Should().MatchRegex(@"ChaveMestraConfiguracao\.AvisarSeFaltar\s*\(",
            "sem chave-mestra o host sobe, mas tem de avisar");
    }
}