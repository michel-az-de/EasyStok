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
        ["aguardando"] = new("Aguardando", "badge-warn",    "warn"),
        ["preparando"] = new("Preparando", "badge-info",    "info"),
        ["pronto"]     = new("Pronto",     "badge-info",    "info"),
        ["entregue"]   = new("Entregue",   "badge-ok",      "ok"),
        ["cancelado"]  = new("Cancelado",  "badge-crit",    "crit"),
        ["em_producao"] = new("Em produção", "badge-info",  "info"),

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
        return new StatusInfo(status, "badge-neutral", "neutral");
    }

    public static string Label(string? status) => Resolve(status).Label;
    public static string BadgeClass(string? status) => Resolve(status).BadgeClass;
}
