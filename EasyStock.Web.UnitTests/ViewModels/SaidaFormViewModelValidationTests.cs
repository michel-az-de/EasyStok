using System.ComponentModel.DataAnnotations;
using EasyStock.Web.Models.ViewModels.Saidas;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.ViewModels;

/// <summary>
/// Trava a validação server-side de Valor na saída (issue 419). A regra
/// "Venda exige &gt; 0" vive no use case da API; aqui garantimos que valor
/// negativo nunca passa, e que 0/null seguem válidos para saída não-venda.
/// </summary>
public class SaidaFormViewModelValidationTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Valor_Negativo_DeveRejeitar()
    {
        var vm = new SaidaFormViewModel { Natureza = "perda", Qty = 1, Valor = -5m };
        Validate(vm).Should().Contain(r => r.MemberNames.Any(m => m == nameof(SaidaFormViewModel.Valor)));
    }

    [Fact]
    public void Valor_Zero_DeveAceitar()
    {
        var vm = new SaidaFormViewModel { Natureza = "perda", Qty = 1, Valor = 0m };
        Validate(vm).Should().NotContain(r => r.MemberNames.Any(m => m == nameof(SaidaFormViewModel.Valor)));
    }

    [Fact]
    public void Valor_Null_DeveAceitar()
    {
        var vm = new SaidaFormViewModel { Natureza = "venda", Qty = 1, Valor = null };
        Validate(vm).Should().NotContain(r => r.MemberNames.Any(m => m == nameof(SaidaFormViewModel.Valor)));
    }
}
