using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutoDetailViewModel
{
    public required ProdutoDetalhe Produto { get; set; }

    public bool TemFicha =>
        !string.IsNullOrWhiteSpace(Produto.AtributosJson) && Produto.AtributosJson.Trim() != "{}";

    public bool MostrarFichaNutricional => Produto.Tipo == 1 || TemFicha;

    private (bool ok, string label, int pts)[] IntegrityFields
    {
        get
        {
            var p = Produto;
            var baseFields = new (bool ok, string label, int pts)[]
            {
                (p.Fotos.Any(),                               "Foto",        20),
                (!string.IsNullOrWhiteSpace(p.DescricaoBase), "Descrição",   15),
                (p.CustoReferencia is > 0,                    "Custo",       15),
                (p.PrecoReferencia is > 0,                    "Preço",       15),
                (!string.IsNullOrWhiteSpace(p.CodigoBarras),  "Cód.Barras",  10),
                (p.Variacoes.Any(),                           "Variações",   10),
                (!string.IsNullOrWhiteSpace(p.Marca),         "Marca",        5),
                (p.Dimensoes != null,                         "Dimensões",    5),
                (true,                                        "Nome",         3),
                (true,                                        "Categoria",    2),
            };
            return MostrarFichaNutricional
                ? [.. baseFields, (TemFicha, "Nutricional", 10)]
                : baseFields;
        }
    }

    public int IntegrityScore => IntegrityFields.Where(f => f.ok).Sum(f => f.pts);

    public List<string> IntegrityMissing =>
        IntegrityFields.Where(f => !f.ok).Select(f => f.label).ToList();

    public static readonly Dictionary<string, int> IntegrityMissingStepMap = new()
    {
        ["Foto"] = 2, ["Preço"] = 2, ["Custo"] = 2,
        ["Variações"] = 3, ["Dimensões"] = 3,
        ["Descrição"] = 1, ["Cód.Barras"] = 1, ["Marca"] = 1, ["Nome"] = 1, ["Categoria"] = 1,
        ["Nutricional"] = 0,
    };

    public string IntegrityBarCss => IntegrityScore >= 80 ? "bg-emerald-500"
        : IntegrityScore >= 50 ? "bg-amber-400" : "bg-red-500";

    public string IntegrityScoreCss => IntegrityScore >= 80 ? "text-emerald-600"
        : IntegrityScore >= 50 ? "text-amber-600" : "text-red-600";
}
