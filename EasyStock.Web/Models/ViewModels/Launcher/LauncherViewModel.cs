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
/// Missão do dia: uma pendência que o operador consegue zerar hoje, computada de dados que
/// o sistema já tem (ADR-0046). Sem tabela, sem estado — <see cref="Concluida"/> é o próprio
/// dado tendo chegado a zero. Missão sem fonte de dado simplesmente não é criada.
/// </summary>
public sealed record MissaoViewModel(
    string Chave,
    string Titulo,
    string Href,
    int Pendentes,
    bool Concluida);

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

    // ── Cards de módulo ──
    public List<ModuloCardViewModel> Modulos { get; set; } = [];

    // ── Missões de hoje ──
    public List<MissaoViewModel> Missoes { get; set; } = [];

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
