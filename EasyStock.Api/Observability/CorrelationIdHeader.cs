namespace EasyStock.Api.Observability;

/// <summary>
/// Correlation id que entra pelo header do cliente. O valor vai para o log e volta na tela de
/// diagnóstico do SuperAdmin (#955): só passa o que cabe num identificador; o resto é trocado
/// por um id gerado aqui, sem recusar a requisição.
/// </summary>
public static class CorrelationIdHeader
{
    public const string Nome = "X-Correlation-Id";
    public const int TamanhoMaximo = 64;

    public static string Resolver(string? recebido) =>
        EhIdentificador(recebido) ? recebido! : Guid.NewGuid().ToString();

    private static bool EhIdentificador(string? valor) =>
        !string.IsNullOrEmpty(valor)
        && valor.Length <= TamanhoMaximo
        && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
