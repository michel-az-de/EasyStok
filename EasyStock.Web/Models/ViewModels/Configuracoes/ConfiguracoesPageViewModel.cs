using EasyStock.Web.Models.ViewModels.ConfiguracaoFiscal;

namespace EasyStock.Web.Models.ViewModels.Configuracoes;

/// <summary>
/// VM da página de Configurações (ADR-0032, fatia 8): compõe a aba "Geral"
/// (alertas/notificações/operacional) e a aba "Fiscal" (NFC-e). Montado eager
/// pelo controller (PATCH-2) p/ o GET e o caminho de erro do POST.
/// </summary>
public sealed record ConfiguracoesPageViewModel(
    ConfiguracoesViewModel Geral,
    ConfiguracaoFiscalViewModel Fiscal);
