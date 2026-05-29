using System.Text.RegularExpressions;
using EasyStock.Application.UseCases.Etiquetas;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class LayoutJsonValidator : AbstractValidator<LayoutJsonDocument>
{
    private static readonly HashSet<string> TiposPermitidos =
        ["text", "image", "code", "nutritional-table", "preparo-text", "alergenos-pills", "divider"];

    private static readonly HashSet<string> TiposMaxUm =
        ["nutritional-table", "preparo-text", "alergenos-pills"];

    private static readonly HashSet<string> AssetsPermitidos =
        ["system:logo-easystok", "system:lockup-easystok", "loja:logo"];

    private static readonly HashSet<string> FontesPermitidas = ["display", "sans", "mono"];
    private static readonly HashSet<string> AlinhasPermitidos = ["left", "center", "right"];

    private static readonly HashSet<string> VariaveisPermitidas =
    [
        "produto.nome", "produto.marca",
        "produto.peso_g", // C2 (RDC 727/2022): peso unitario obrigatorio para Embalado.
        "produto.ficha.kcal", "produto.ficha.proteina_g", "produto.ficha.carbs_g",
        "produto.ficha.gordura_g", "produto.ficha.gordura_saturada_g",
        "produto.ficha.fibras_g", "produto.ficha.sodio_mg", "produto.ficha.porcao_g",
        "etiqueta.codigo", "etiqueta.sequencial",
        "lote.codigo", "lote.validadeEm", "lote.criadoEm",
        "empresa.nome",
    ];

    private static readonly Regex VarRegex =
        new(@"\{([a-z]+(?:\.[a-z_A-Z]+)*)(?::[^}]+)?\}", RegexOptions.Compiled);

    public LayoutJsonValidator()
    {
        RuleFor(x => x.V).Equal(1).WithMessage("Versão do layout deve ser 1.");

        RuleFor(x => x.Size).NotNull().WithMessage("size é obrigatório.");
        When(x => x.Size != null, () =>
        {
            RuleFor(x => x.Size.WMm).GreaterThan(0).WithMessage("size.w_mm deve ser > 0.");
            RuleFor(x => x.Size.HMm).GreaterThan(0).WithMessage("size.h_mm deve ser > 0.");
            RuleFor(x => x.Size.Orientation)
                .Must(o => o == "horizontal" || o == "vertical")
                .WithMessage("size.orientation deve ser 'horizontal' ou 'vertical'.");
        });

        RuleFor(x => x.Elements).NotNull().NotEmpty().WithMessage("elements não pode estar vazio.");

        // Tipo whitelist + Id obrigatório
        RuleForEach(x => x.Elements)
            .Must(e => !string.IsNullOrEmpty(e.Id))
            .WithMessage((_, e) => $"Elemento sem id.");

        RuleForEach(x => x.Elements)
            .Must(e => TiposPermitidos.Contains(e.Type))
            .WithMessage((_, e) => $"Tipo '{e.Type}' não é permitido.");

        // Bounds — acesso ao doc pai via (doc, e)
        When(x => x.Size != null, () =>
        {
            RuleForEach(x => x.Elements)
                .Must((doc, e) => e.XMm + e.WMm <= doc.Size.WMm)
                .WithMessage((doc, e) => $"Elemento '{e.Id}' ultrapassa a largura do layout.");

            RuleForEach(x => x.Elements)
                .Must((doc, e) => e.YMm + e.HMm <= doc.Size.HMm)
                .WithMessage((doc, e) => $"Elemento '{e.Id}' ultrapassa a altura do layout.");
        });

        // Text
        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "text" || !string.IsNullOrEmpty(e.Content))
            .WithMessage((_, e) => $"Elemento '{e.Id}': content é obrigatório para text.");

        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "text" || e.Content == null || e.Content.Length <= 500)
            .WithMessage((_, e) => $"Elemento '{e.Id}': content ≤ 500 chars.");

        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "text" || !e.SizePt.HasValue || EstaNoRodape(e) || e.SizePt >= 8)
            .WithMessage((_, e) => $"Elemento '{e.Id}': size_pt mínimo 8pt fora do rodapé.");

        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "text" || e.Font == null || FontesPermitidas.Contains(e.Font))
            .WithMessage((_, e) => $"Elemento '{e.Id}': font deve ser 'display', 'sans' ou 'mono'.");

        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "text" || e.Align == null || AlinhasPermitidos.Contains(e.Align))
            .WithMessage((_, e) => $"Elemento '{e.Id}': align deve ser 'left', 'center' ou 'right'.");

        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "text" || string.IsNullOrEmpty(e.Content) || ValidarVariaveis(e.Content))
            .WithMessage((_, e) => $"Elemento '{e.Id}': variável inválida no content.");

        // Image
        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "image" || (!string.IsNullOrEmpty(e.Asset) && AssetsPermitidos.Contains(e.Asset!)))
            .WithMessage((_, e) => $"Elemento '{e.Id}': asset '{e.Asset}' não é permitido.");

        // Code — format
        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "code" || (e.Format == "qr" || e.Format == "barcode-code128" || e.Format == "barcode-ean13"))
            .WithMessage((_, e) => $"Elemento '{e.Id}': format inválido.");

        // Code — tamanho mínimo QR
        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "code" || e.Format != "qr" || (e.WMm >= 18 && e.HMm >= 18))
            .WithMessage((_, e) => $"Elemento '{e.Id}': QR mínimo 18×18mm.");

        // Code — tamanho mínimo barcode
        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "code" || (e.Format != "barcode-code128" && e.Format != "barcode-ean13") || (e.WMm >= 30 && e.HMm >= 10))
            .WithMessage((_, e) => $"Elemento '{e.Id}': barcode mínimo 30×10mm.");

        // Code — content obrigatório
        RuleForEach(x => x.Elements)
            .Must(e => e.Type != "code" || !string.IsNullOrEmpty(e.Content))
            .WithMessage((_, e) => $"Elemento '{e.Id}': content é obrigatório para code.");

        // Cardinalidade
        RuleFor(x => x.Elements)
            .Must(els => els.Count(e => e.Type == "code") <= 2)
            .WithMessage("Máximo 2 elementos code por layout.");

        foreach (var tipo in TiposMaxUm)
        {
            var t = tipo;
            RuleFor(x => x.Elements)
                .Must(els => els.Count(e => e.Type == t) <= 1)
                .WithMessage($"Máximo 1 elemento '{tipo}' por layout.");
        }
    }

    private static bool EstaNoRodape(LayoutElement e) =>
        e.SizePt.HasValue && e.SizePt < 8 && e.YMm >= 35;

    private static bool ValidarVariaveis(string content)
    {
        foreach (Match m in VarRegex.Matches(content))
        {
            if (!VariaveisPermitidas.Contains(m.Groups[1].Value))
                return false;
        }
        return true;
    }
}
