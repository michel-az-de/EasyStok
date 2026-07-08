using System.Runtime.CompilerServices;

namespace EasyStock.Web.Helpers;

/// <summary>
/// Vocabulario canonico de status, com label + classe de badge + nivel semantico.
/// Use <see cref="Resolve"/> em Razor views para renderizar consistente.
/// </summary>
public record StatusInfo(string Label, string BadgeClass, string SemanticLevel);

public static class StatusHelper
{
    private static readonly StatusInfo Empty = new("—", "badge-neutral", "neutral");

    private static readonly Dictionary<string, StatusInfo> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ----- Pedidos -----
        // Esquema de-enfatiza estados terminais (epic #534): cor chama atencao no que
        // precisa de acao (aguardando/preparando); pronto=positivo; entregue/cancelado calmos.
        ["aguardando"] = new("Aguardando", "badge-warn",    "warn"),
        ["preparando"] = new("Em preparo", "badge-info",    "info"),  // canônico (issue 863): StatusPedidoVocabulario.RotuloLojista
        ["pronto"]     = new("Pronto",     "badge-ok",      "ok"),
        ["entregue"]   = new("Entregue",   "badge-neutral", "neutral"),
        ["cancelado"]  = new("Cancelado",  "badge-neutral badge-struck", "neutral"),
        ["em_producao"] = new("Em produção", "badge-info",  "info"),
        // Fluxo Storefront guest (ADR-0014): StatusPedidoMapper emite estas 4 strings
        // que o Web não conhecia → status cru sem badge (QA v1.10 BUG-003/014/015).
        // Poka-yoke: StatusHelperCobreTodosOsStatusPedido (Web.UnitTests) trava o drift.
        ["rascunho"]                  = new("Rascunho",             "badge-neutral", "neutral"),
        ["aguardando_pagamento"]      = new("Aguardando pagamento", "badge-warn",    "warn"),
        ["aguardando_aprovacao_baba"] = new("Aguardando aprovação", "badge-warn",    "warn"),
        ["aprovado_baba"]             = new("Aprovado",             "badge-info",    "info"),

        // ----- Lotes -----
        ["finalizado"] = new("Finalizado", "badge-ok",      "ok"),
        ["expirado"]   = new("Expirado",   "badge-crit",    "crit"),
        ["vencido"]    = new("Vencido",    "badge-crit",    "crit"),
        ["disponivel"] = new("Disponível", "badge-ok",      "ok"),

        // ----- Caixa -----
        ["aberto"]            = new("Aberto",  "badge-ok",      "ok"),
        ["fechado"]           = new("Fechado", "badge-neutral", "neutral"),
        ["fechado-pendente"]  = new("Aguardando abertura", "badge-warn", "warn"),
        ["fechado_pendente"]  = new("Aguardando abertura", "badge-warn", "warn"),

        // ----- Comum -----
        ["critico"] = new("Crítico", "badge-crit", "crit"),
    };

    public static StatusInfo Resolve(string? status, [CallerMemberName] string caller = "")
    {
        if (string.IsNullOrWhiteSpace(status)) return Empty;
        if (Map.TryGetValue(status, out var info)) return info;
        // status orfao = bug rastreavel. Felipe ve no debug output do .NET ou nos logs do fly.
        System.Diagnostics.Debug.WriteLine($"[StatusHelper] Status órfão: '{status}' em {caller}");
#if DEBUG
        // Em dev, falha ALTO: foi o fallback silencioso (string crua + badge-neutral) que
        // deixou o BUG-003 (QA v1.10) passar. Em prod degrada suave; em dev grita na hora.
        return new StatusInfo($"UNMAPPED:{status}", "badge-crit", "crit");
#else
        return new StatusInfo(status, "badge-neutral", "neutral");
#endif
    }

    public static string Label(string? status) => Resolve(status).Label;
    public static string BadgeClass(string? status) => Resolve(status).BadgeClass;
}
