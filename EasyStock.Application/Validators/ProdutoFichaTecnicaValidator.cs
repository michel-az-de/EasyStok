using FluentValidation;

namespace EasyStock.Application.Validators;

public sealed record ProdutoFichaTecnicaCommand(
    Guid EmpresaId,
    Guid ProdutoId,
    decimal? PorcaoG,
    decimal? Kcal,
    decimal? CarbsG,
    decimal? ProteinaG,
    decimal? GorduraG,
    decimal? GorduraSaturadaG,
    decimal? FibrasG,
    decimal? SodioMg,
    string? ModoPreparo,
    List<string>? Ingredientes,
    List<string>? Alergenos,
    string? AlergenosOutros
);

public class ProdutoFichaTecnicaValidator : AbstractValidator<ProdutoFichaTecnicaCommand>
{
    private static readonly HashSet<string> AlergenosValidos =
        ["gluten", "lactose", "ovo", "soja", "amendoim", "castanhas", "peixe", "crustaceos", "outros"];

    public ProdutoFichaTecnicaValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.ProdutoId).NotEmpty();

        // Nutricionais: opcional, mas se informado deve ser ≥ 0 e ≤ 9999
        var numericos = new[] { "PorcaoG", "Kcal", "CarbsG", "ProteinaG", "GorduraG", "GorduraSaturadaG", "FibrasG", "SodioMg" };
        RuleFor(x => x.PorcaoG).InclusiveBetween(0, 9999).When(x => x.PorcaoG.HasValue);
        RuleFor(x => x.Kcal).InclusiveBetween(0, 9999).When(x => x.Kcal.HasValue);
        RuleFor(x => x.CarbsG).InclusiveBetween(0, 9999).When(x => x.CarbsG.HasValue);
        RuleFor(x => x.ProteinaG).InclusiveBetween(0, 9999).When(x => x.ProteinaG.HasValue);
        RuleFor(x => x.GorduraG).InclusiveBetween(0, 9999).When(x => x.GorduraG.HasValue);
        RuleFor(x => x.GorduraSaturadaG).InclusiveBetween(0, 9999).When(x => x.GorduraSaturadaG.HasValue);
        RuleFor(x => x.FibrasG).InclusiveBetween(0, 9999).When(x => x.FibrasG.HasValue);
        RuleFor(x => x.SodioMg).InclusiveBetween(0, 9999).When(x => x.SodioMg.HasValue);

        RuleFor(x => x.ModoPreparo)
            .MaximumLength(4000).When(x => x.ModoPreparo != null)
            .WithMessage("Modo de preparo deve ter no máximo 4000 caracteres.");

        RuleFor(x => x.Ingredientes)
            .Must(i => i == null || i.Count <= 50)
            .WithMessage("Máximo 50 ingredientes.")
            .When(x => x.Ingredientes != null);

        RuleForEach(x => x.Ingredientes)
            .MaximumLength(200)
            .When(x => x.Ingredientes != null)
            .WithMessage("Cada ingrediente deve ter no máximo 200 caracteres.");

        RuleFor(x => x.Alergenos)
            .Must(a => a == null || a.TrueForAll(v => AlergenosValidos.Contains(v)))
            .WithMessage($"Alérgenos inválidos. Valores permitidos: {string.Join(", ", AlergenosValidos)}.");

        // Regra separada por tipo: .When() encadeado na mesma linha afeta toda a chain
        RuleFor(x => x.AlergenosOutros)
            .NotEmpty()
            .WithMessage("AlergenosOutros é obrigatório quando 'outros' está marcado.")
            .When(x => x.Alergenos != null && x.Alergenos.Contains("outros"));

        RuleFor(x => x.AlergenosOutros)
            .MaximumLength(200)
            .When(x => x.AlergenosOutros != null);
    }
}
