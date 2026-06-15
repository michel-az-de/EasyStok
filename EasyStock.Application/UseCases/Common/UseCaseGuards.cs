using System.Text.RegularExpressions;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Helpers defensivos reutilizáveis em use cases para centralizar validações
/// estruturais (IDs obrigatórios, tenant scope, etc.) que antes eram
/// replicadas em cada handler.
/// </summary>
public static class UseCaseGuards
{
    /// <summary>
    /// Garante que o <paramref name="empresaId"/> foi informado. Usado no topo
    /// de use cases que têm escopo multi-tenant.
    /// </summary>
    public static void EnsureEmpresaId(Guid empresaId)
    {
        if (empresaId == Guid.Empty)
            throw new UseCaseValidationException("EmpresaId é obrigatório.");
    }

    /// <summary>
    /// Variante para IDs obrigatórios genéricos. Mensagem customizável para
    /// ajudar o cliente a identificar qual campo faltou.
    /// </summary>
    public static void EnsureNotEmpty(Guid id, string nomeCampo)
    {
        if (id == Guid.Empty)
            throw new UseCaseValidationException($"{nomeCampo} é obrigatório.");
    }

    // Detecta inicio de tag HTML: '<' seguido de letra, '/', '!' ou '?' (ex.: <script>, </b>, <!--).
    // NAO casa '<'/'>' isolados — nomes legitimos como "Tamanho > M" ou "Loja <3" passam.
    private static readonly Regex TagHtmlPattern = new(@"<[a-zA-Z/!?]", RegexOptions.Compiled);

    /// <summary>
    /// Rejeita tags HTML (&lt;tag&gt;) em campos de texto livre exibidos ao usuário.
    /// Defesa em profundidade contra XSS armazenado (BUG-05 do QA): a saída já escapa em
    /// HTML (Razor), mas contextos como PDF/etiqueta/exportação podem não escapar —
    /// bloquear tags na entrada cobre todos de uma vez. '&lt;'/'&gt;' isolados são
    /// liberados para não rejeitar nomes legítimos.
    /// </summary>
    public static void EnsureSemTagsHtml(string? texto, string nomeCampo)
    {
        if (!string.IsNullOrEmpty(texto) && TagHtmlPattern.IsMatch(texto))
            throw new UseCaseValidationException($"{nomeCampo} não pode conter tags HTML.");
    }

    // Remove sequencias tag-like ('<' + letra/'/'/'!'/'?' ate o proximo '>', com ou sem
    // fechamento): mesma deteccao do TagHtmlPattern, em forma de remocao. Preserva
    // '<'/'>' isolados (ex.: "Tamanho > M", "Loja <3").
    private static readonly Regex TagHtmlStripPattern = new(@"<[a-zA-Z/!?][^>]*>?", RegexOptions.Compiled);

    /// <summary>
    /// Versão sanitizadora do <see cref="EnsureSemTagsHtml"/>: REMOVE tags HTML em vez de
    /// lançar. Para caminhos que não podem rejeitar a entrada (ex.: auto-link mobile em
    /// background), mas onde ainda não queremos persistir markup que vazaria em
    /// PDF/etiqueta/exportação. Preserva '&lt;'/'&gt;' isolados.
    /// </summary>
    public static string? RemoverTagsHtml(string? texto)
        => string.IsNullOrEmpty(texto) ? texto : TagHtmlStripPattern.Replace(texto, string.Empty);
}
