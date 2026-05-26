using System.ComponentModel.DataAnnotations;
using EasyStock.Admin.Pages.Storefronts;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Admin.Storefronts;

/// <summary>
/// Trava validações client-side do form de edição de storefront — cor primária
/// em hex e WhatsApp em E.164. Form completo é coberto via E2E (fora do escopo
/// desta task, ver materialização ADMIN-001).
/// </summary>
public class EditPageModelValidationTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("#E85814")]
    [InlineData("#000")]
    [InlineData("#FFF")]
    [InlineData("#abc")]
    [InlineData("#abcdef")]
    [InlineData(null)] // null = não tocar, válido
    public void CorPrimaria_Validas(string? cor)
    {
        var input = new EditModel.EditInput { CorPrimaria = cor };
        var errors = Validate(input);
        errors.Should().NotContain(r => r.MemberNames.Any(m => m == nameof(EditModel.EditInput.CorPrimaria)));
    }

    [Theory]
    [InlineData("E85814")]       // sem #
    [InlineData("#GG0000")]      // não-hex
    [InlineData("#EE")]          // tamanho errado
    [InlineData("vermelho")]     // texto
    public void CorPrimaria_Invalidas(string cor)
    {
        var input = new EditModel.EditInput { CorPrimaria = cor };
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(EditModel.EditInput.CorPrimaria)));
    }

    [Theory]
    [InlineData("+5511997573992")]
    [InlineData("5511997573992")]
    [InlineData("1234567890")] // 10 dígitos mínimo
    public void WhatsApp_Validos(string wa)
    {
        var input = new EditModel.EditInput { WhatsappPedidos = wa };
        var errors = Validate(input);
        errors.Should().NotContain(r => r.MemberNames.Any(m => m == nameof(EditModel.EditInput.WhatsappPedidos)));
    }

    [Theory]
    [InlineData("11-99757-3992")]   // hífens
    [InlineData("abc")]
    [InlineData("123")]              // muito curto
    public void WhatsApp_Invalidos(string wa)
    {
        var input = new EditModel.EditInput { WhatsappPedidos = wa };
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(EditModel.EditInput.WhatsappPedidos)));
    }

    [Fact]
    public void PedidoMinimo_Negativo_DeveFalhar()
    {
        var input = new EditModel.EditInput { PedidoMinimoEntrega = -1m };
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(EditModel.EditInput.PedidoMinimoEntrega)));
    }
}
