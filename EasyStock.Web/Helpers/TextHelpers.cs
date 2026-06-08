namespace EasyStock.Web.Helpers;

/// <summary>
/// Pluralização pt-BR para a UI. Substitui o padrão "item(ns)" / "entrada(s)"
/// dos templates por flexão real ("1 item" / "2 itens"), o detalhe que separa
/// produto polido de template cru (pente-fino UX 2026-06-07).
/// </summary>
public static class TextHelpers
{
    /// <summary>Número + palavra flexionada: <c>Plural(1, "item", "itens")</c> => "1 item".
    /// Param <c>long</c> aceita int e long por widening (contadores variam).</summary>
    public static string Plural(long n, string singular, string plural)
        => $"{n} {(n == 1 ? singular : plural)}";

    /// <summary>Só a palavra flexionada (sem número), p/ quando o número já é exibido à parte.</summary>
    public static string PluralWord(long n, string singular, string plural)
        => n == 1 ? singular : plural;
}
