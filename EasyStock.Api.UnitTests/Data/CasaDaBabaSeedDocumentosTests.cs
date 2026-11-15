using System.Text.RegularExpressions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Data;

/// <summary>
/// #2679 (K2, homologação #1769): todo CPF e CNPJ do seed demo da Casa da Baba passa no mesmo
/// dígito verificador que os use cases exigem ao salvar. Com documento inválido no seed, nenhum
/// fornecedor de demonstração podia ser editado no laboratório.
/// </summary>
public class CasaDaBabaSeedDocumentosTests
{
    private static readonly Regex DocumentoFormatado =
        new(@"""(\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}|\d{3}\.\d{3}\.\d{3}-\d{2})""", RegexOptions.Compiled);

    [Fact]
    public void DocumentosDoSeed_TemDigitoVerificadorValido()
    {
        var fonte = File.ReadAllText(CaminhoDoSeed());
        var documentos = DocumentoFormatado.Matches(fonte).Select(m => m.Groups[1].Value).ToList();

        documentos.Should().NotBeEmpty("o seed demo cadastra empresa e fornecedores com documento");
        var invalidos = documentos.Where(d => Cnpj.TemFormaDeCnpj(d) ? !Cnpj.EhValido(d) : !Cpf.EhValido(d)).ToList();
        invalidos.Should().BeEmpty("o use case recusa salvar cadastro com dígito verificador errado");
    }

    private static string CaminhoDoSeed()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyStok.sln"))
                                && !File.Exists(Path.Combine(dir.FullName, "easystock-back.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("o teste roda dentro do repositório");
        return Path.Combine(dir!.FullName, "EasyStock.Api", "Data", "Tenants", "CasaDaBabaSeed.cs");
    }
}
