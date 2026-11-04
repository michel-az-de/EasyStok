using EasyStock.Application.UseCases.ImpressaoLoja;
using FluentValidation;

namespace EasyStock.Application.Validators;

/// <summary>
/// Validação estrita do PUT da regra de impressão (#2199). Cada erro aponta o caminho do campo
/// (<c>documentos.pedido.papel</c>), o mesmo da fixture <c>docs/contracts/impressao-loja.v1.fixture.json</c>.
/// </summary>
public sealed class RegraImpressaoLojaValidator : AbstractValidator<RegraImpressaoLojaEntrada>
{
    public RegraImpressaoLojaValidator()
    {
        RuleFor(x => x.V)
            .Equal(RegraImpressaoCatalogo.Versao)
            .OverridePropertyName("v")
            .WithMessage("v deve ser 1.");

        RuleFor(x => x.Documentos)
            .NotNull()
            .OverridePropertyName("documentos")
            .WithMessage("documentos é obrigatório.");

        RuleFor(x => x.Documentos).Custom((documentos, ctx) =>
        {
            if (documentos is null) return;
            foreach (var (nome, doc) in documentos)
            {
                var caminho = $"documentos.{nome}";
                if (!RegraImpressaoCatalogo.Documentos.Contains(nome))
                {
                    ctx.AddFailure(caminho, $"Documento '{nome}' não existe. Use: {Lista(RegraImpressaoCatalogo.Documentos)}.");
                    continue;
                }

                if (doc is null)
                {
                    ctx.AddFailure(caminho, "Documento não pode ser nulo. Para usar o padrão, retire a chave.");
                    continue;
                }

                ValidarDocumento(caminho, doc, ctx);
            }
        });
    }

    private static void ValidarDocumento(
        string caminho, RegraImpressaoDocumentoEntrada doc, ValidationContext<RegraImpressaoLojaEntrada> ctx)
    {
        if (doc.Papel is null || !RegraImpressaoCatalogo.Papeis.Contains(doc.Papel))
            ctx.AddFailure($"{caminho}.papel", $"papel deve ser um de: {Lista(RegraImpressaoCatalogo.Papeis)}.");

        if (doc.Orientacao is not null && !RegraImpressaoCatalogo.Orientacoes.Contains(doc.Orientacao))
            ctx.AddFailure($"{caminho}.orientacao", "orientacao deve ser 'vertical', 'horizontal' ou null (herda do template).");

        if (doc.Codigos is not null && doc.Codigos.Any(c => c is null || !RegraImpressaoCatalogo.Codigos.Contains(c)))
            ctx.AddFailure($"{caminho}.codigos", $"codigos aceita só: {Lista(RegraImpressaoCatalogo.Codigos)}.");

        if (doc.Copias is < RegraImpressaoCatalogo.CopiasMin or > RegraImpressaoCatalogo.CopiasMax)
            ctx.AddFailure($"{caminho}.copias",
                $"copias deve ficar entre {RegraImpressaoCatalogo.CopiasMin} e {RegraImpressaoCatalogo.CopiasMax}.");

        if (doc.Template is null) return;
        if (doc.Template.Origem is null || !RegraImpressaoCatalogo.OrigensTemplate.Contains(doc.Template.Origem))
            ctx.AddFailure($"{caminho}.template.origem", "template.origem deve ser 'Sistema' ou 'Empresa'.");
        if (doc.Template.Id is null || doc.Template.Id == Guid.Empty)
            ctx.AddFailure($"{caminho}.template.id", "template.id é obrigatório.");
    }

    private static string Lista(IEnumerable<string> valores) => string.Join(", ", valores);
}
