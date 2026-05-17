using EasyStock.Application.UseCases.Etiquetas;
using EasyStock.Application.Validators;

namespace EasyStock.Application.Tests.Validators;

/// <summary>
/// ≥30 casos para o LayoutJsonValidator (F6).
/// Cobre: versão, size, tipos, cardinalidade, bounds, fontes, QR, barcode, variáveis, asset.
/// </summary>
public class LayoutJsonValidatorTests
{
    private static readonly LayoutJsonValidator _v = new();

    // ── helpers ────────────────────────────────────────────────────────────

    private static LayoutJsonDocument DocValido() => new(
        V: 1,
        Size: new("80x40mm", 80, 40, "horizontal"),
        Elements:
        [
            Texto("t1", "{produto.nome}", 14, 0, 0, 40, 8),
            QR("qr1", 60, 2, 18, 18),
        ]);

    private static LayoutElement Texto(string id, string content, decimal sizePt,
        decimal x, decimal y, decimal w, decimal h, string? font = "sans", string? align = "left") =>
        new(id, "text", content, font, sizePt, null, null, 400, align, "shrink-then-ellipsis",
            null, null, null, null, x, y, w, h, false);

    private static LayoutElement QR(string id, decimal x, decimal y, decimal w, decimal h) =>
        new(id, "code", "{etiqueta.codigo}", null, null, null, null, null, null, null,
            null, null, "qr", 1, x, y, w, h, false);

    private static LayoutElement Barcode(string id, string format, decimal x, decimal y, decimal w, decimal h) =>
        new(id, "code", "{etiqueta.codigo}", null, null, null, null, null, null, null,
            null, null, format, null, x, y, w, h, false);

    private static LayoutElement Imagem(string id, string asset, decimal x, decimal y, decimal w, decimal h) =>
        new(id, "image", null, null, null, null, null, null, null, null,
            null, asset, null, null, x, y, w, h, false);

    private static LayoutElement Divider(string id, decimal x, decimal y, decimal w) =>
        new(id, "divider", null, null, null, null, null, null, null, null,
            null, null, null, null, x, y, w, 1, false);

    private static LayoutElement Tipo(string tipo, string id) =>
        new(id, tipo, null, null, null, null, null, null, null, null,
            null, null, null, null, 0, 0, 10, 6, false);

    // ── Versão ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Versao_Invalida_Falha()
    {
        var doc = DocValido() with { V = 2 };
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("Versão"));
    }

    [Fact]
    public async Task Versao_Um_Valida()
    {
        var r = await _v.ValidateAsync(DocValido());
        r.IsValid.Should().BeTrue();
    }

    // ── Size ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Size_Null_Falha()
    {
        var doc = new LayoutJsonDocument(1, null!, DocValido().Elements);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Size_LarguraZero_Falha()
    {
        var doc = DocValido() with { Size = new("x", 0, 40, "horizontal") };
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Size_OrientacaoInvalida_Falha()
    {
        var doc = DocValido() with { Size = new("x", 80, 40, "diagonal") };
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    public async Task Size_OrientacaoValida_Passa(string orientacao)
    {
        var doc = DocValido() with { Size = new("x", 80, 40, orientacao) };
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    // ── Elementos obrigatórios ─────────────────────────────────────────────

    [Fact]
    public async Task Elements_Vazio_Falha()
    {
        var doc = DocValido() with { Elements = [] };
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Element_SemId_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("", "{produto.nome}", 10, 0, 0, 20, 6));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    // ── Tipos whitelisted ──────────────────────────────────────────────────

    [Fact]
    public async Task Tipo_Desconhecido_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(new("x1", "botao-secreto", null, null, null, null, null,
            null, null, null, null, null, null, null, 0, 0, 10, 5, false));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("botao-secreto"));
    }

    [Theory]
    [InlineData("nutritional-table")]
    [InlineData("preparo-text")]
    [InlineData("alergenos-pills")]
    [InlineData("divider")]
    public async Task Tipos_Validos_Passam(string tipo)
    {
        var doc = new LayoutJsonDocument(1,
            new("x", 80, 40, "horizontal"),
            [Tipo(tipo, "el1")]);
        var r = await _v.ValidateAsync(doc);
        // Não valida erro de tipo — pode falhar por outras regras (ex: text sem content)
        r.Errors.Should().NotContain(e => e.ErrorMessage.Contains(tipo));
    }

    // ── Bounds ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bounds_UltrapassaLargura_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t2", "{produto.marca}", 10, 70, 0, 20, 5)); // 70+20=90 > 80
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("largura"));
    }

    [Fact]
    public async Task Bounds_UltrapassaAltura_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t3", "{produto.marca}", 10, 0, 38, 20, 5)); // 38+5=43 > 40
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("altura"));
    }

    [Fact]
    public async Task Bounds_ExatoNaLargura_Passa()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t4", "{produto.marca}", 10, 60, 0, 20, 5)); // 60+20=80 == 80
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    // ── Fonte e texto ──────────────────────────────────────────────────────

    [Fact]
    public async Task Texto_SizePtMenorQue8_ForaRodape_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t5", "{produto.marca}", 7, 0, 10, 30, 5)); // y=10, não é rodapé
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("size_pt"));
    }

    [Fact]
    public async Task Texto_SizePt6_NoRodape_Passa()
    {
        // y=36 >= 35 → considera rodapé (regra EstaNoRodape: y_mm >= 35)
        var doc = DocValido();
        doc.Elements.Add(Texto("footer", "@easystok", 6, 60, 36, 16, 3));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Texto_FonteInvalida_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t6", "{produto.nome}", 10, 0, 20, 30, 5, font: "comic-sans"));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Texto_AlinhamentoInvalido_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t7", "{produto.nome}", 10, 0, 20, 30, 5, align: "justify"));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Texto_ContentVazio_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t8", "", 10, 0, 20, 30, 5));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("content é obrigatório"));
    }

    [Fact]
    public async Task Texto_ContentMuitoLongo_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t9", new string('x', 501), 10, 0, 20, 30, 5));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("500"));
    }

    // ── Variáveis ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Variavel_Invalida_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("t10", "{produto.xyz}", 10, 0, 20, 30, 5));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("variável inválida"));
    }

    [Theory]
    [InlineData("{produto.nome}")]
    [InlineData("{etiqueta.codigo}")]
    [InlineData("{lote.validadeEm:dd/MM/yyyy}")]
    [InlineData("{empresa.nome}")]
    [InlineData("{produto.ficha.kcal}")]
    public async Task Variavel_Valida_Passa(string variavel)
    {
        var doc = DocValido();
        doc.Elements.Add(Texto("tvv", variavel, 10, 0, 20, 30, 5));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    // ── Image asset ────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_AssetInvalido_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(Imagem("img1", "https://malicious.com/logo.png", 2, 2, 10, 5));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("asset"));
    }

    [Theory]
    [InlineData("system:logo-easystok")]
    [InlineData("system:lockup-easystok")]
    [InlineData("loja:logo")]
    public async Task Image_AssetValido_Passa(string asset)
    {
        var doc = DocValido();
        doc.Elements.Add(Imagem("img2", asset, 2, 2, 10, 5));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    // ── Code: QR ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QR_MenorQue18mm_Falha()
    {
        var doc = new LayoutJsonDocument(1,
            new("x", 80, 40, "horizontal"),
            [Texto("t1", "{produto.nome}", 10, 0, 0, 40, 8), QR("qr", 60, 2, 10, 10)]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("18×18mm"));
    }

    [Fact]
    public async Task QR_Exato18x18_Passa()
    {
        var doc = new LayoutJsonDocument(1,
            new("x", 80, 40, "horizontal"),
            [Texto("t1", "{produto.nome}", 10, 0, 0, 40, 8), QR("qr", 60, 2, 18, 18)]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    // ── Code: barcode ──────────────────────────────────────────────────────

    [Fact]
    public async Task Barcode128_MenorQue30x10_Falha()
    {
        var doc = new LayoutJsonDocument(1,
            new("x", 80, 40, "horizontal"),
            [Texto("t1", "{produto.nome}", 10, 0, 0, 40, 8), Barcode("bc", "barcode-code128", 2, 20, 20, 8)]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("barcode mínimo"));
    }

    [Fact]
    public async Task Barcode128_Valido_Passa()
    {
        var doc = new LayoutJsonDocument(1,
            new("x", 80, 40, "horizontal"),
            [Texto("t1", "{produto.nome}", 10, 0, 0, 40, 8), Barcode("bc", "barcode-code128", 2, 20, 30, 10)]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Code_FormatInvalido_Falha()
    {
        var doc = DocValido();
        doc.Elements.Add(new("c2", "code", "{etiqueta.codigo}", null, null, null, null,
            null, null, null, null, null, "pdf417", null, 0, 20, 18, 18, false));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("format inválido"));
    }

    // ── Cardinalidade ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cardinalidade_TresCode_Falha()
    {
        var doc = new LayoutJsonDocument(1, new("x", 100, 50, "horizontal"),
        [
            Texto("t1", "{produto.nome}", 10, 0, 0, 40, 8),
            QR("qr1", 0, 10, 18, 18),
            QR("qr2", 20, 10, 18, 18),
            QR("qr3", 40, 10, 18, 18),
        ]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("Máximo 2"));
    }

    [Fact]
    public async Task Cardinalidade_DoisCode_Passa()
    {
        var doc = new LayoutJsonDocument(1, new("x", 80, 40, "horizontal"),
        [
            Texto("t1", "{produto.nome}", 10, 0, 0, 40, 8),
            QR("qr1", 0, 10, 18, 18),
            QR("qr2", 20, 10, 18, 18),
        ]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("nutritional-table")]
    [InlineData("preparo-text")]
    [InlineData("alergenos-pills")]
    public async Task Cardinalidade_DoisDoMaxUm_Falha(string tipo)
    {
        var doc = new LayoutJsonDocument(1, new("x", 80, 40, "horizontal"),
        [
            Tipo(tipo, "el1"),
            Tipo(tipo, "el2"),
        ]);
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains($"Máximo 1 elemento '{tipo}'"));
    }

    // ── Divider ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Divider_Valido_Passa()
    {
        var doc = DocValido();
        doc.Elements.Add(Divider("div1", 2, 19, 56));
        var r = await _v.ValidateAsync(doc);
        r.IsValid.Should().BeTrue();
    }
}
