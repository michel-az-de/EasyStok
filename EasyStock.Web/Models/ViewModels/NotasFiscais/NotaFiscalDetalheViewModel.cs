using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.NotasFiscais;

public class NotaFiscalDetalheViewModel
{
    public NfeInfo Nfe { get; set; } = new();
    public List<NfeItemInfo> Itens { get; set; } = [];
    public List<NfeEventoInfo> Eventos { get; set; } = [];

    public bool PodeCancelar =>
        string.Equals(Nfe.Status, "Autorizada", StringComparison.OrdinalIgnoreCase)
        && Nfe.DataAutorizacao.HasValue
        && (DateTime.UtcNow - Nfe.DataAutorizacao.Value).TotalHours < 24;

    public string StatusLabel => (Nfe.Status ?? "").ToLowerInvariant() switch
    {
        "autorizada" => "Autorizada",
        "cancelada" => "Cancelada",
        "rejeitada" => "Rejeitada",
        "falhatransiente" => "Em contingencia",
        "enviada" => "Aguardando",
        "rascunho" => "Rascunho",
        "inutilizada" => "Inutilizada",
        _ => Nfe.Status ?? "Desconhecido"
    };

    public string StatusCss => (Nfe.Status ?? "").ToLowerInvariant() switch
    {
        "autorizada" => "badge-ok",
        "cancelada" or "inutilizada" => "badge-neutral",
        "rejeitada" => "badge-crit",
        "falhatransiente" => "badge-warn",
        "enviada" => "badge-info",
        _ => "badge-neutral"
    };
}
