using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guards da #2018: cada regra de negócio que estava copiada em vários pontos do backend tem um dono só, e um
/// segundo ponto escrevendo a regra de novo reprova o build. Source-text (idioma de <see cref="EmailFabricaUnicaTests"/>),
/// comentários ignorados. A prova red-bar (injetar a violação, ver o teste falhar, reverter) está na descrição da PR.
/// </summary>
[Trait("Category", "Architecture")]
public class RegraUnicaNoBackendTests
{
    private static readonly string[] SufixosDeProjetoDeTeste =
        [".Tests", ".UnitTests", ".IntegrationTests", ".ArchitectureTests"];

    // ---- Número curto do pedido -------------------------------------------------------------------------------

    private static readonly Regex NumeroCurtoEscritoAMao = new(@"ToString\(""N""\)\[\.\.8\]", RegexOptions.Compiled);

    /// <summary>Outros ids (item do caderno do agente, ping do diagnóstico, produto do Web congelado), não o pedido.</summary>
    private static readonly string[] NumeroCurtoDeOutroId =
    [
        "EasyStock.Domain/Entities/Atendimento/TrechoCaderno.cs",
        "EasyStock.Api/Controllers/DiagnosticoController.cs",
        "EasyStock.Web/Models/Api/SugestaoReposicaoApi.cs",
    ];

    [Fact]
    public void NumeroCurtoDoPedido_SoEscritoPeloCodigoCurto()
    {
        var root = RepoPaths.FindRepoRoot();
        var fora = ArquivosDeProducao(root, "*.cs")
            .Where(f => NumeroCurtoEscritoAMao.IsMatch(SemComentarios(File.ReadAllText(f))))
            .Select(f => Relativo(root, f))
            .Where(r => r != "EasyStock.Domain/ValueObjects/CodigoCurto.cs" && !NumeroCurtoDeOutroId.Contains(r))
            .ToList();

        fora.Should().BeEmpty(
            "o número curto do pedido é CodigoCurto.De(id) (8 hexadecimais em maiúsculas); escrever Id.ToString(\"N\")[..8] "
            + "de novo traz de volta o financeiro em minúsculo e o canhoto em maiúsculo para o mesmo pedido");
    }

    // ---- Fuso de Brasília -------------------------------------------------------------------------------------

    /// <summary>O Web (Razor) está congelado (ADR-0059) e tem o espelho BrazilTime.</summary>
    private static readonly string[] FusoNoWebCongelado = ["EasyStock.Web/Helpers/BrazilTime.cs"];

    [Fact]
    public void FusoDeBrasilia_SoResolvidoPeloOperacionalFuso()
    {
        var root = RepoPaths.FindRepoRoot();
        var fora = ArquivosDeProducao(root, "*.cs")
            .Where(f => SemComentarios(File.ReadAllText(f)).Contains("FindSystemTimeZoneById", StringComparison.Ordinal))
            .Select(f => Relativo(root, f))
            .Where(r => r != "EasyStock.Domain/Defaults/OperacionalFuso.cs" && !FusoNoWebCongelado.Contains(r))
            .ToList();

        fora.Should().BeEmpty(
            "o fuso de Brasília é resolvido por OperacionalFuso (Domain); HorarioBrasil e os demais delegam. Uma segunda "
            + "resolução volta a dar hora errada (UTC) em ambiente sem tzdata e escapa do StartupHardening.ValidateTimezone");
    }

    // ---- CEP e bairro -----------------------------------------------------------------------------------------

    private static readonly Regex NormalizadorDeEnderecoProprio = new(
        @"static\s+string\s+Normalizar(?:Cep|Bairro)\s*\(", RegexOptions.Compiled);

    /// <summary>O hash de idempotência do checkout multi-gateway sai na #2015 (código fantasma): não se mexe nele aqui.</summary>
    private static readonly string[] CepNoCodigoFantasma = ["EasyStock.Application/UseCases/Storefront/Checkout/Idempotency/CheckoutContentHasher.cs"];

    [Fact]
    public void CepEBairro_SoNormalizadosPeloEnderecoBr()
    {
        var root = RepoPaths.FindRepoRoot();
        var fora = ArquivosDeProducao(root, "*.cs")
            .Where(f => NormalizadorDeEnderecoProprio.IsMatch(SemComentarios(File.ReadAllText(f))))
            .Select(f => Relativo(root, f))
            .Where(r => r != "EasyStock.Domain/ValueObjects/EnderecoBr.cs" && !CepNoCodigoFantasma.Contains(r))
            .ToList();

        fora.Should().BeEmpty(
            "CEP e bairro são normalizados por EnderecoBr.NormalizarCep/NormalizarBairro (Domain); uma cópia a mais volta a "
            + "deixar o frete, as janelas e o checkout comparando CEPs de formas diferentes");
    }

    // ---- Apoio ------------------------------------------------------------------------------------------------

    private static string Relativo(string root, string arquivo) =>
        Path.GetRelativePath(root, arquivo).Replace(Path.DirectorySeparatorChar, '/');

    private static string SemComentarios(string conteudo) =>
        string.Join('\n', conteudo.Split('\n').Where(linha =>
        {
            var t = linha.TrimStart();
            return !(t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*"));
        }));

    private static IEnumerable<string> ArquivosDeProducao(string root, string padrao) =>
        Directory.GetDirectories(root, "EasyStock.*")
            .Where(d => !SufixosDeProjetoDeTeste.Any(s => Path.GetFileName(d).EndsWith(s, StringComparison.Ordinal)))
            .SelectMany(d => Directory.GetFiles(d, padrao, SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar));
}
