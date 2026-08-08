using System.Globalization;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Launcher;
using EasyStock.Web.Services;

namespace EasyStock.Web.Navigation;

/// <summary>
/// Builder PURO (sem HttpContext, sem Api) do portal (ADR-0046): recebe os resumos ja
/// buscados e devolve o <see cref="LauncherViewModel"/> pronto. Mesmo desenho do
/// <see cref="MenuViewModelBuilder"/> — a logica de apresentacao fica testavel e o
/// controller vira casca fina.
/// </summary>
public static class LauncherViewModelBuilder
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static LauncherViewModel Montar(
        DateTime agoraBrasil,
        string? usuarioNome,
        DashboardResumoApi? dash,
        ResumoDiaApi? dia,
        MenuBadges badges,
        IReadOnlyList<MenuItemView> meuDia,
        IReadOnlyList<ModuloInfo> modulos,
        DashboardFinanceiroApi? financeiro = null)
    {
        var vm = new LauncherViewModel
        {
            Saudacao = Saudar(agoraBrasil, usuarioNome),
            DataHoje = agoraBrasil.ToString("dddd, d 'de' MMMM 'de' yyyy", PtBr),
            MeuDia = [.. meuDia],
            Modulos = [.. modulos.Select(m => CriarCard(m, badges, financeiro))],
            Missoes = MontarMissoes(badges, dia, financeiro),
        };

        if (dash is not null)
        {
            // Mesma contagem do badge do Dashboard no menu (criticos + vencidos): o portal
            // e o menu nunca discordam sobre quantos alertas existem.
            var alertas = badges.DashboardTotal;
            vm.StatusTone = alertas > 0 ? "critical" : dash.AlertasItensParados > 0 ? "warning" : "ok";
            vm.StatusMsg = vm.StatusTone switch
            {
                "critical" => alertas == 1
                    ? "1 alerta precisa de atenção."
                    : $"{alertas} alertas precisam de atenção.",
                "warning" => dash.AlertasItensParados == 1
                    ? "1 item parado há mais de 30 dias."
                    : $"{dash.AlertasItensParados} itens parados há mais de 30 dias.",
                _ => "Estoque saudável. Tudo certo por aqui.",
            };
        }

        if (dia is not null)
        {
            vm.FaturamentoHoje = dia.FaturamentoHoje;
            vm.PedidosEntreguesHoje = dia.PedidosEntreguesHoje;
            vm.CaixaAbertoHoje = dia.CaixaAbertaHoje;
            vm.CaixaFechadoHoje = dia.CaixaFechadaHoje;
            vm.SaldoCaixaAtual = dia.SaldoCaixaAtual;
            vm.PedidosPendentes = dia.PedidosPendentes;
            vm.ValorPedidosPendentes = dia.ValorPedidosPendentes;
        }

        return vm;
    }

    /// <summary>
    /// "Bom dia, Felipe. 👋" — primeiro nome apenas. Nome vazio ou so espacos cai em
    /// "por aqui" em vez de renderizar "Bom dia, ." (a sessao devolve "" quando o claim
    /// de nome nao veio no token).
    /// </summary>
    private static string Saudar(DateTime agora, string? usuarioNome)
    {
        var hora = agora.Hour;
        var saudacao = hora < 12 ? "Bom dia" : hora < 18 ? "Boa tarde" : "Boa noite";

        var primeiro = usuarioNome?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(primeiro)
            ? $"{saudacao} por aqui. 👋"
            : $"{saudacao}, {primeiro}. 👋";
    }

    /// <summary>
    /// Missoes de hoje: pendencias computadas do que o sistema ja sabe, sem tabela nem
    /// estado — "concluida" e o proprio dado ter chegado a zero. Missao cuja fonte nao
    /// respondeu nao entra na lista (em vez de aparecer falsamente concluida).
    /// </summary>
    private static List<MissaoViewModel> MontarMissoes(
        MenuBadges badges, ResumoDiaApi? dia, DashboardFinanceiroApi? financeiro)
    {
        var missoes = new List<MissaoViewModel>
        {
            Missao("pedidos", "Zerar os pedidos em aberto", "/pedidos", badges.PedidosAbertos),
            Missao("validade", "Resolver os lotes vencidos", "/estoque?status=vencido", badges.LotesVencidos),
            Missao("estoque-critico", "Repor o estoque crítico", "/estoque", badges.ProdutosCriticos),
        };

        if (dia is not null)
        {
            // Caixa nao tem contagem: ou o dia foi aberto (ou ja fechado), ou esta pendente.
            var caixaResolvido = dia.CaixaAbertaHoje || dia.CaixaFechadaHoje;
            missoes.Add(new MissaoViewModel(
                "caixa", "Abrir o caixa do dia", "/caixa",
                caixaResolvido ? 0 : 1, caixaResolvido));
        }

        if (financeiro is not null)
        {
            // QtdParcelasVencidasHoje e combinado (pagar + receber) — o titulo diz isso e o
            // link leva a visao geral, em vez de fingir uma desagregacao que nao temos.
            missoes.Add(Missao(
                "parcelas-vencidas", "Tratar as parcelas vencidas hoje", "/financeiro",
                financeiro.QtdParcelasVencidasHoje));
        }

        return missoes;
    }

    private static MissaoViewModel Missao(string chave, string titulo, string href, int pendentes) =>
        new(chave, titulo, href, pendentes, pendentes == 0);

    private static ModuloCardViewModel CriarCard(ModuloInfo m, MenuBadges badges, DashboardFinanceiroApi? financeiro)
    {
        var alertasEstoque = badges.ProdutosCriticos + badges.LotesVencidos;

        var (badge, badgeType, status) = m.Key switch
        {
            "operacao" => (badges.PedidosAbertos, badges.PedidosAbertos > 0 ? "crit" : "ok",
                badges.PedidosAbertos switch
                {
                    0 => "Tudo em ordem",
                    1 => "1 pedido em aberto",
                    var n => $"{n} pedidos em aberto",
                }),
            "producao" => (alertasEstoque, alertasEstoque > 0 ? "warn" : "ok",
                alertasEstoque switch
                {
                    0 => "Tudo em ordem",
                    1 => "1 alerta",
                    var n => $"{n} alertas",
                }),
            "financeiro" when financeiro is not null => (
                financeiro.QtdParcelasVencidasHoje,
                financeiro.QtdParcelasVencidasHoje > 0 ? "warn" : "ok",
                financeiro.QtdParcelasVencidasHoje switch
                {
                    0 => "Em dia",
                    1 => "1 parcela vencida hoje",
                    var n => $"{n} parcelas vencidas hoje",
                }),
            // Sem fonte de contagem (ou financeiro fora do ar): card sem numero e sem
            // status. Nunca inventar dado.
            _ => (0, "ok", string.Empty),
        };

        return new ModuloCardViewModel(
            m.Key, m.Nome, m.IconeLucide, m.Descricao, m.HrefDefault,
            badge, badgeType, status);
    }
}
