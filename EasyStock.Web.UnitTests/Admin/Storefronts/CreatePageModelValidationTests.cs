using System.ComponentModel.DataAnnotations;
using EasyStock.Admin.Pages.Storefronts;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Admin.Storefronts;

/// <summary>
/// Trava validações client-side do form de criação de storefront. Não testa
/// chamadas HTTP — chamadas reais do AdminApiClient são cobertas pelos
/// integration tests (AdminStorefrontControllerTests).
/// </summary>
public class CreatePageModelValidationTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("casa-da-baba")]
    [InlineData("ab1")]
    [InlineData("cliente-001")]
    [InlineData("a123456789012345678901234567890123456789")] // exatamente 40 chars (1 letter + 38 digits + 1 digit)
    public void Slug_DeveAceitar_FormasValidas(string slug)
    {
        var input = NewInput(slug: slug);
        Validate(input).Should().NotContain(r => r.MemberNames.Any(m => m == nameof(CreateModel.CreateInput.Slug)));
    }

    [Theory]
    [InlineData("")]              // vazio
    [InlineData("AB")]            // menos de 3 chars
    [InlineData("Casa-Da-Baba")]  // uppercase
    [InlineData("-comeca-hifen")] // começa com hífen
    [InlineData("termina-")]      // termina com hífen
    [InlineData("hi--fens")]      // hífens consecutivos
    [InlineData("espaco no slug")]
    public void Slug_DeveRejeitar_FormasInvalidas(string slug)
    {
        var input = NewInput(slug: slug);
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(CreateModel.CreateInput.Slug)));
    }

    [Fact]
    public void EmpresaId_Vazio_NaoDeveValidar()
    {
        var input = NewInput(empresaId: Guid.Empty);
        var errors = Validate(input);
        // EmpresaId é [Required] — mas Guid.Empty passa o Required default; aqui o teste
        // documenta o gap: backend (CriarStorefrontAdminUseCase) já valida Guid.Empty
        // como inválido. Frontend confia no backend.
        errors.Should().NotContain(r => r.MemberNames.Any(m => m == nameof(CreateModel.CreateInput.EmpresaId)));
    }

    [Fact]
    public void TituloPublico_Vazio_DeveFalhar()
    {
        var input = NewInput(titulo: "");
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(CreateModel.CreateInput.TituloPublico)));
    }

    [Fact]
    public void TituloPublico_AcimaDe120_DeveFalhar()
    {
        var input = NewInput(titulo: new string('A', 121));
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(CreateModel.CreateInput.TituloPublico)));
    }

    [Fact]
    public void PedidoMinimo_Negativo_DeveFalhar()
    {
        var input = NewInput(pedidoMin: -1);
        var errors = Validate(input);
        errors.Should().Contain(r => r.MemberNames.Any(m => m == nameof(CreateModel.CreateInput.PedidoMinimoEntrega)));
    }

    private static CreateModel.CreateInput NewInput(
        Guid? empresaId = null,
        string? slug = null,
        string? titulo = null,
        decimal? pedidoMin = null) =>
        new()
        {
            EmpresaId = empresaId ?? Guid.NewGuid(),
            Slug = slug ?? "casa-da-baba",
            TituloPublico = titulo ?? "Casa da Babá",
            PedidoMinimoEntrega = pedidoMin ?? 0m,
        };
}
