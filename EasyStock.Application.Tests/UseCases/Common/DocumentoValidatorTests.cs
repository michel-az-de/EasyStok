namespace EasyStock.Application.Tests.UseCases.Common;

/// <summary>
/// BUG-01: gate de documento (CPF/CNPJ) server-side. Valida dígito verificador quando o
/// documento tem forma de CPF (11) ou CNPJ (14); outros comprimentos seguem tolerados.
/// </summary>
public class DocumentoValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureValido_no_op_para_vazio(string? doc)
    {
        var act = () => DocumentoValidator.EnsureValido(doc);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("111.444.777-35")]       // CPF válido
    [InlineData("11144477735")]
    [InlineData("11.222.333/0001-81")]   // CNPJ válido
    [InlineData("11222333000181")]
    [InlineData("04.252.011/0001-10")]
    public void EnsureValido_aceita_cpf_e_cnpj_validos(string doc)
    {
        var act = () => DocumentoValidator.EnsureValido(doc);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("12345678900")]          // CPF dígito verificador errado
    [InlineData("11111111111")]          // CPF repetido
    public void EnsureValido_rejeita_cpf_invalido(string doc)
    {
        var act = () => DocumentoValidator.EnsureValido(doc, "Documento do cliente");
        act.Should().Throw<UseCaseValidationException>().WithMessage("*CPF*");
    }

    [Theory]
    [InlineData("11111111111111")]       // CNPJ repetido — caso do QA (BUG-01)
    [InlineData("11222333000180")]       // CNPJ dígito verificador errado
    public void EnsureValido_rejeita_cnpj_invalido(string doc)
    {
        var act = () => DocumentoValidator.EnsureValido(doc, "CNPJ/CPF do fornecedor");
        act.Should().Throw<UseCaseValidationException>().WithMessage("*CNPJ*");
    }

    [Theory]
    [InlineData("1111111111")]           // 10 dígitos — tolerado (estrangeiro/legado)
    [InlineData("ABC123")]               // não-numérico curto — tolerado
    public void EnsureValido_tolera_outros_comprimentos(string doc)
    {
        var act = () => DocumentoValidator.EnsureValido(doc);
        act.Should().NotThrow();
    }
}
