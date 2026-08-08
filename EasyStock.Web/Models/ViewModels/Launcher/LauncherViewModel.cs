using EasyStock.Web.Navigation;

namespace EasyStock.Web.Models.ViewModels.Launcher;

/// <summary>
/// Card de módulo exibido no launcher (portal de entrada).
/// Cada card representa um grupo funcional do menu (Operação, Estoque, etc.)
/// com sua contagem de alertas e status resumido.
/// </summary>
public sealed record ModuloCardViewModel(
    string Key,
    string Nome,
    string Icone,
    string Descricao,
    string Href,
    int BadgeCount,
    string BadgeType,   // crit | warn | ok | info
    string StatusText
);

/// <summary>
/// ViewModel do launcher: cockpit de entrada do sistema.
/// Reutiliza os mesmos dados do Dashboard (resumo do dia, alertas, KPIs)
/// mas apresentados como portal de módulos em vez de dashboard tradicional.
/// </summary>
public sealed class LauncherViewModel
{
    public string Saudacao { get; set; } = string.Empty;
    public string DataHoje { get; set; } = string.Empty;
    public string StatusMsg { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "ok";

    // ── Atenção do dia (cards acionáveis) ──
    public int PedidosAbertos { get; set; }
    public int LotesVencendo { get; set; }
    public int ContasVencerHoje { get; set; }

    // ── Cards de módulo ──
    public List<ModuloCardViewModel> Modulos { get; set; } = [];

    // ── Meu dia (favoritos) ──
    public List<MenuItemView> MeuDia { get; set; } = [];

    // ── Pulso de hoje (KPIs rápidos) ──
    public decimal FaturamentoHoje { get; set; }
    public int PedidosEntreguesHoje { get; set; }
    public bool CaixaAbertoHoje { get; set; }
    public bool CaixaFechadoHoje { get; set; }
    public decimal SaldoCaixaAtual { get; set; }
    public int PedidosPendentes { get; set; }
    public decimal ValorPedidosPendentes { get; set; }
}
