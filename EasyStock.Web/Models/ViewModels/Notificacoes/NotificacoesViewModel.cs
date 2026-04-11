using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Notificacoes;

public class NotificacoesViewModel
{
    public List<Notificacao> Items { get; set; } = [];
    public int NaoLidas { get; set; }
    public NotificacaoResumo? Resumo { get; set; }
    public string? FiltroTipo { get; set; }
    public string? FiltroSeveridade { get; set; }
}
