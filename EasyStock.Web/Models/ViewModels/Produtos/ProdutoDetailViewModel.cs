using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutoDetailViewModel
{
    public required ProdutoDetalhe Produto { get; set; }

    public bool TemFicha =>
        !string.IsNullOrWhiteSpace(Produto.AtributosJson) && Produto.AtributosJson.Trim() != "{}";

    public bool MostrarFichaNutricional => Produto.Tipo == 1 || TemFicha;

    // #582 / ADR-0033: completude agora vem do backend (fonte unica) — lista e detalhe exibem
    // o MESMO valor. O calculo ponderado vive em Produto.CalcularCompletude (dominio).
    public int IntegrityScore => Produto.CompletudePercent;

    public List<string> IntegrityMissing => Produto.Pendencias.ToList();

    public static readonly Dictionary<string, int> IntegrityMissingStepMap = new()
    {
        ["Foto"] = 2, ["Preço"] = 2, ["Custo"] = 2,
        ["Dimensões"] = 3,
        ["Descrição"] = 1, ["Cód.Barras"] = 1, ["Marca"] = 1, ["Nome"] = 1, ["Categoria"] = 1,
        ["Nutricional"] = 0,
    };

    public string IntegrityBarCss => IntegrityScore >= 80 ? "bg-emerald-500"
        : IntegrityScore >= 50 ? "bg-amber-400" : "bg-red-500";

    public string IntegrityScoreCss => IntegrityScore >= 80 ? "text-emerald-600"
        : IntegrityScore >= 50 ? "text-amber-600" : "text-red-600";
}
