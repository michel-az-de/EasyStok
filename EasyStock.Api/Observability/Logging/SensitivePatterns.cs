using System.Text.RegularExpressions;

namespace EasyStock.Api.Observability.Logging;

/// <summary>
/// Fonte de verdade unica das regex de mascaramento de dados sensiveis em logs.
///
/// Consumida por:
/// - B3.0b (esta feature) <see cref="RedactingTextFormatter"/> — write-time, intercepta
///   antes de escrever no arquivo. Cobre vetor at-rest/shipping (Datadog/Sentry/backup).
/// - B3.0a (proxima feature) ISensitivePatternMasker.RegexSensitivePatternMasker — read-time
///   no analyzer de logs do endpoint /diagnostico. Cobre vetor de exibicao no SSE.
///
/// Os dois redactores precisam dividir EXATAMENTE este set. Se a regex falhar aqui,
/// falha nos dois caminhos juntos (failure mode coerente, nao silencioso).
/// </summary>
public static class SensitivePatterns
{
    /// <summary>String de substituicao constante — facil de greppar em prod.</summary>
    public const string RedactedToken = "[REDACTED]";

    /// <summary>
    /// Regex pre-compiladas (Compiled flag) por performance. Enricher Serilog roda
    /// em todo log event; alocacao por chamada seria custosa.
    /// </summary>
    public static readonly IReadOnlyList<Regex> Patterns = new[]
    {
        // Pares chave=valor / chave: valor com chave sensivel.
        // Captura "password=abc123", "Senha: foo", "ApiKey=xyz", "token: eyJ..."
        // Substituicao preserva a chave para facilitar debugging — so o valor vira REDACTED.
        // (NB: "bearer" e "authorization" NAO entram aqui — sao tratados pelo Padrao 4
        // porque o separador comum e espaco, nao =/:)
        new Regex(
            @"(?i)\b(password|senha|secret|apikey|api_key|api-key|token|connectionstring|conn[_-]?str)\s*[=:]\s*\S+",
            RegexOptions.Compiled),

        // Connection string Postgres/SQL Server completa.
        // Captura "Host=db;Password=segredo;..." preservando o Host.
        new Regex(
            @"(?i)Host=[^;]+;.*?Password=[^;\s]+",
            RegexOptions.Compiled),

        // JWT bearer cru no log (sem prefixo Bearer). Tres segmentos base64url separados por ponto.
        // Conservador no length (>=20 chars por segmento) para nao redactar UUIDs.
        new Regex(
            @"\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\b",
            RegexOptions.Compiled),

        // Headers Authorization completos: "Bearer <token>" ou "Authorization: Bearer <token>".
        // Separador entre Bearer e o token e espaco — o Padrao 1 (chave=valor) nao alcanca
        // porque \S+ para no proprio "Bearer". Esta regex captura "Bearer <token>" inteiro.
        new Regex(
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled),
    };

    /// <summary>
    /// Aplica todas as regex no input, retornando string com segredos substituidos.
    /// Para chave=valor: preserva a chave, substitui o valor. Caso geral: substitui o match inteiro.
    /// </summary>
    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = input;
        // Padrao 1 (chave=valor): preserva chave, troca valor.
        result = Patterns[0].Replace(result, m =>
        {
            var raw = m.Value;
            var sepIndex = raw.IndexOfAny(['=', ':']);
            if (sepIndex < 0) return RedactedToken;
            var key = raw[..(sepIndex + 1)];
            return $"{key}{RedactedToken}";
        });
        // Padrao 2 (conn string): preserva Host=..., redacta Password=...
        result = Patterns[1].Replace(result, m =>
        {
            var raw = m.Value;
            var pwdIdx = raw.IndexOf("Password", StringComparison.OrdinalIgnoreCase);
            if (pwdIdx < 0) return RedactedToken;
            return $"{raw[..pwdIdx]}Password={RedactedToken}";
        });
        // Padrao 3 (JWT cru): substitui match inteiro.
        result = Patterns[2].Replace(result, RedactedToken);
        // Padrao 4 (Bearer <token>): preserva o "Bearer", substitui o token.
        result = Patterns[3].Replace(result, $"Bearer {RedactedToken}");
        return result;
    }
}
