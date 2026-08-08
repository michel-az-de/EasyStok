using System.Globalization;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Launcher;
using EasyStock.Web.Navigation;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Launcher (portal de módulos): tela de entrada pós-login que apresenta
/// os módulos do tenant como cards acionáveis, com resumo do dia e atenção
/// imediata. Substitui o Dashboard como destino padrão pós-autenticação.
/// </summary>
public class LauncherController(ApiClient api, SessionService session) : BaseController(session)
{
    // NAO reivindicar "/": a raiz e da landing publica (SiteController), que redireciona
    // pra ca quando ha sessao. Dois attribute routes com o mesmo template derrubariam
    // GET / com AmbiguousMatchException, inclusive para visitante anonimo.
    [HttpGet("/launcher")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Portal";
        ViewBag.ActiveMenuItem = "Launcher";

        var empresaId = session.GetEmpresaId() ?? string.Empty;
        var lojaId = session.GetLojaId() ?? string.Empty;
        var usuarioNome = session.GetUsuarioNome() ?? "Usuário";

        // ── Saudação e data (fuso BR, mesmo padrão do Dashboard) ──
        var agoraBrasil = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        var hora = agoraBrasil.Hour;
        var saudacao = hora < 12 ? "Bom dia" : hora < 18 ? "Boa tarde" : "Boa noite";
        var ptBR = new CultureInfo("pt-BR");

        var vm = new LauncherViewModel
        {
            Saudacao = $"{saudacao}, {usuarioNome.Split(' ').First()}. 👋",
            DataHoje = agoraBrasil.ToString("dddd, d 'de' MMMM 'de' yyyy", ptBR),
        };

        // ── Busca resumo do dia (reusa endpoints do Dashboard) ──
        var dashTask = api.GetAsync<DashboardResumoApi>("analytics/dashboard");
        var diaTask = api.GetAsync<ResumoDiaApi>("analytics/dia");

        var dashResult = await dashTask;
        var diaResult = await diaTask;

        if (dashResult.Success && dashResult.Data is { } d)
        {
            vm.StatusTone = d.AlertasEstoqueBaixo > 0 || d.AlertasVencimento > 0 ? "critical" :
                            d.AlertasItensParados > 0 ? "warning" : "ok";
            vm.StatusMsg = vm.StatusTone switch
            {
                "critical" => $"{d.AlertasEstoqueBaixo + d.AlertasVencimento} alertas precisam de atenção.",
                "warning" => $"{d.AlertasItensParados} itens parados há mais de 30 dias.",
                _ => "Estoque saudável. Tudo certo por aqui."
            };
        }

        if (diaResult.Success && diaResult.Data is { } dia)
        {
            vm.FaturamentoHoje = dia.FaturamentoHoje;
            vm.PedidosEntreguesHoje = dia.PedidosEntreguesHoje;
            vm.CaixaAbertoHoje = dia.CaixaAbertaHoje;
            vm.CaixaFechadoHoje = dia.CaixaFechadaHoje;
            vm.SaldoCaixaAtual = dia.SaldoCaixaAtual;
            vm.PedidosPendentes = dia.PedidosPendentes;
            vm.ValorPedidosPendentes = dia.ValorPedidosPendentes;
            vm.PedidosAbertos = dia.PedidosPendentes; // mesmo dado, rotulo diferente
        }

        // ── Contas a vencer (financeiro) ──
        // Simplificado: busca do endpoint de contas a receber quando houver
        vm.ContasVencerHoje = 0; // placeholder — será preenchido quando o endpoint existir

        // ── Monta cards de módulo ──
        var modulosDef = ModuloDefinition.PorEmpresa(empresaId);
        // Mesma composicao do MenuResumoService (ADR-0032, fatia 2): pedidos vem do resumo
        // do dia, criticos e vencidos do dashboard. Em F4 a chamada direta ao ApiClient sai
        // e o servico com cache passa a ser a unica fonte.
        var badges = dashResult.Success && dashResult.Data is { } db
            ? new MenuBadges(
                PedidosAbertos: diaResult.Success ? diaResult.Data?.PedidosPendentes ?? 0 : 0,
                ProdutosCriticos: db.AlertasEstoqueBaixo,
                LotesVencidos: db.AlertasVencidos)
            : MenuBadges.Zero;

        vm.Modulos = modulosDef.Select(m => CriarCard(m, badges)).ToList();

        // ── Meu dia (favoritos) ──
        // Reusa MenuViewModelBuilder com o menu atual (sem filtro de módulo)
        var favoritos = new List<string>(); // TODO: buscar de PreferenciaMenuService
        var menuVm = MenuViewModelBuilder.Build(null, null, favoritos, badges, true);
        vm.MeuDia = [.. menuVm.MeuDia];

        return View(vm);
    }

    private static ModuloCardViewModel CriarCard(ModuloInfo m, MenuBadges badges)
    {
        var (badge, badgeType, status) = m.Key switch
        {
            "operacao" => (badges.PedidosAbertos, badges.PedidosAbertos > 0 ? "crit" : "ok",
                           badges.PedidosAbertos > 0 ? $"{badges.PedidosAbertos} pedidos em aberto" : "Tudo em ordem"),
            "producao" => (badges.LotesVencidos + badges.ProdutosCriticos, badges.LotesVencidos + badges.ProdutosCriticos > 0 ? "warn" : "ok",
                           badges.LotesVencidos + badges.ProdutosCriticos > 0 ? $"{badges.LotesVencidos + badges.ProdutosCriticos} alertas" : "Tudo em ordem"),
            // Financeiro ainda nao tem contagem aqui: sem numero e melhor que numero inventado.
            // O badge real (parcelas vencidas hoje) entra em F7, via financeiro/dashboard.
            _ => (0, "ok", "")
        };

        return new ModuloCardViewModel(
            m.Key, m.Nome, m.IconeLucide, m.Descricao, m.HrefDefault,
            badge, badgeType, status, m.CorClasse);
    }
}
