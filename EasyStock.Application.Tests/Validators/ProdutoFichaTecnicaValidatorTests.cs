using EasyStock.Application.Validators;

namespace EasyStock.Application.Tests.Validators;

/// <summary>
/// ≥15 casos para ProdutoFichaTecnicaValidator (F6).
/// </summary>
public class ProdutoFichaTecnicaValidatorTests
{
    private static readonly ProdutoFichaTecnicaValidator _v = new();

    private static readonly Guid EmpId  = Guid.NewGuid();
    private static readonly Guid ProdId = Guid.NewGuid();

    private static ProdutoFichaTecnicaCommand CmdValido() => new(
        EmpresaId: EmpId, ProdutoId: ProdId,
        PorcaoG: 100, Kcal: 250, CarbsG: 30, ProteinaG: 12,
        GorduraG: 8, GorduraSaturadaG: 3, FibrasG: 2, SodioMg: 400,
        ModoPreparo: "Aquecer por 5 min.",
        Ingredientes: ["farinha", "ovo"],
        Alergenos: ["gluten"],
        AlergenosOutros: null);

    // ── IDs obrigatórios ───────────────────────────────────────────────────

    [Fact]
    public async Task EmpresaId_Vazio_Falha()
    {
        var cmd = CmdValido() with { EmpresaId = Guid.Empty };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ProdutoId_Vazio_Falha()
    {
        var cmd = CmdValido() with { ProdutoId = Guid.Empty };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
    }

    // ── Comando válido completo ────────────────────────────────────────────

    [Fact]
    public async Task CmdValido_Passa()
    {
        var r = await _v.ValidateAsync(CmdValido());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CmdNulo_TudoOpcional_Passa()
    {
        var cmd = new ProdutoFichaTecnicaCommand(EmpId, ProdId,
            null, null, null, null, null, null, null, null,
            null, null, null, null);
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeTrue();
    }

    // ── Numéricos ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Kcal_Negativo_Falha()
    {
        var cmd = CmdValido() with { Kcal = -1 };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Kcal_AcimaLimite_Falha()
    {
        var cmd = CmdValido() with { Kcal = 10000 };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Kcal_Exato9999_Passa()
    {
        var cmd = CmdValido() with { Kcal = 9999 };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task SodioMg_Zero_Passa()
    {
        var cmd = CmdValido() with { SodioMg = 0 };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeTrue();
    }

    // ── Modo de preparo ────────────────────────────────────────────────────

    [Fact]
    public async Task ModoPreparo_MuitoLongo_Falha()
    {
        var cmd = CmdValido() with { ModoPreparo = new string('x', 4001) };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("4000"));
    }

    [Fact]
    public async Task ModoPreparo_Exato4000_Passa()
    {
        var cmd = CmdValido() with { ModoPreparo = new string('x', 4000) };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeTrue();
    }

    // ── Ingredientes ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ingredientes_51Itens_Falha()
    {
        var cmd = CmdValido() with { Ingredientes = Enumerable.Range(1, 51).Select(i => $"item{i}").ToList() };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("50"));
    }

    [Fact]
    public async Task Ingrediente_MuitoLongo_Falha()
    {
        var cmd = CmdValido() with { Ingredientes = [new string('a', 201)] };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
    }

    // ── Alérgenos ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Alergeno_Invalido_Falha()
    {
        var cmd = CmdValido() with { Alergenos = ["nozes-de-macadamia"] };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("Alérgenos inválidos"));
    }

    [Fact]
    public async Task Alergenos_Outros_SemDescricao_Falha()
    {
        var cmd = CmdValido() with { Alergenos = ["outros"], AlergenosOutros = null };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("AlergenosOutros"));
    }

    [Fact]
    public async Task Alergenos_Outros_ComDescricao_Passa()
    {
        var cmd = CmdValido() with { Alergenos = ["outros"], AlergenosOutros = "Macadâmia" };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("gluten")]
    [InlineData("lactose")]
    [InlineData("soja")]
    [InlineData("amendoim")]
    [InlineData("castanhas")]
    [InlineData("peixe")]
    [InlineData("crustaceos")]
    [InlineData("ovo")]
    public async Task Alergeno_Valido_Passa(string alergeno)
    {
        var cmd = CmdValido() with { Alergenos = [alergeno] };
        var r = await _v.ValidateAsync(cmd);
        r.IsValid.Should().BeTrue();
    }
}
