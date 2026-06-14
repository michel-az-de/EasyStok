using System.Collections.Frozen;

namespace EasyStock.Application.UseCases.GerenciarUploads;

/// <summary>
/// Validação de segurança para uploads: protege contra path traversal no nome do arquivo
/// e restringe o ContentType a uma whitelist. Ambos devem ser invocados antes de qualquer
/// IO pelo storage concreto (S3, Azure, Local).
/// </summary>
public static class UploadSecurityValidator
{
    /// <summary>
    /// MIME types aceitos. Propositadamente conservador — imagens web, PDF, CSV, XLSX.
    /// Adicione com cuidado: qualquer MIME listado aqui pode ser servido como conteúdo público.
    /// </summary>
    public static readonly FrozenSet<string> AllowedMimeTypes = new[]
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "application/pdf",
        "text/csv",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retorna o nome de arquivo saneado — apenas o segmento final, sem separadores de caminho,
    /// sem caracteres null e com tamanho razoável. Lança <see cref="ArgumentException"/> se
    /// o nome for vazio, contiver sequências de path traversal ou ficar vazio após a sanitização.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Nome do arquivo nao pode ser vazio.", nameof(fileName));

        var trimmed = fileName.Trim();

        if (trimmed.Contains('\0'))
            throw new ArgumentException("Nome do arquivo contem caractere null.", nameof(fileName));

        // Rejeita sequências de path traversal antes de sanitizar — evita "safe" bogus como "..%2fx"
        if (trimmed.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Nome do arquivo contem sequencia de path traversal.", nameof(fileName));

        // Path.GetFileName remove qualquer componente de diretório ("a/b/c.jpg" -> "c.jpg").
        // Também normaliza separators do Windows.
        var normalized = Path.GetFileName(trimmed.Replace('\\', '/'));

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Nome do arquivo invalido apos sanitizacao.", nameof(fileName));

        // Rejeita nomes de diretório tipo "." ou ".." (mesmo após sanitize).
        // Nome precisa de algum conteudo antes do ponto.
        if (normalized is "." or "..")
            throw new ArgumentException("Nome do arquivo invalido (apenas pontos).", nameof(fileName));

        // Comprimento defensivo: 255 é o limite comum de filesystems; nomes muito longos sugerem ataque.
        if (normalized.Length > 255)
            throw new ArgumentException("Nome do arquivo excede 255 caracteres.", nameof(fileName));

        // Caracteres invalidos em qualquer plataforma sensata (pegamos ambos sets Windows+Unix
        // pra coerencia cross-platform; no Linux Path.GetInvalidFileNameChars retorna só \0).
        foreach (var invalid in WindowsInvalidChars)
        {
            if (normalized.Contains(invalid))
                throw new ArgumentException(
                    $"Nome do arquivo contem caractere invalido: '{invalid}'.", nameof(fileName));
        }

        return normalized;
    }

    // Uniao dos caracteres invalidos em Windows (mais restritivo) — aplicado
    // em todas as plataformas para comportamento consistente.
    private static readonly char[] WindowsInvalidChars =
    [
        '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007',
        '\u0008', '\u0009', '\u000A', '\u000B', '\u000C', '\u000D', '\u000E', '\u000F',
        '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017',
        '\u0018', '\u0019', '\u001A', '\u001B', '\u001C', '\u001D', '\u001E', '\u001F',
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    /// <summary>
    /// Verifica se <paramref name="contentType"/> consta em <see cref="AllowedMimeTypes"/>.
    /// Lança <see cref="InvalidOperationException"/> quando rejeitado — InvalidOperation em vez
    /// de ArgumentException para permitir tratamento distinto no handler global.
    /// </summary>
    public static void EnsureValidMime(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new InvalidOperationException("ContentType nao informado no upload.");

        // Remove parâmetros (ex.: "text/csv; charset=utf-8" -> "text/csv")
        var mainType = contentType.Split(';', 2)[0].Trim();

        if (!AllowedMimeTypes.Contains(mainType))
            throw new InvalidOperationException(
                $"ContentType '{mainType}' nao permitido. Tipos aceitos: {string.Join(", ", AllowedMimeTypes)}.");
    }

    /// <summary>
    /// Valida que os PRIMEIROS BYTES do conteudo batem com a assinatura (magic number)
    /// do tipo declarado. Defesa contra arquivo malicioso renomeado (ex.: HTML/SVG/EXE
    /// com ContentType image/jpeg) que seria servido publico do bucket (XSS armazenado,
    /// content smuggling). So rejeita quando a assinatura do tipo e CONHECIDA e nao
    /// corresponde; tipos sem assinatura confiavel (ex.: text/csv) passam de proposito.
    /// Chame APOS <see cref="EnsureValidMime"/>.
    /// </summary>
    public static void EnsureContentMatchesDeclaredType(byte[]? content, string? contentType)
    {
        if (content is null || content.Length == 0)
            throw new InvalidOperationException("Conteudo do upload vazio.");
        if (string.IsNullOrWhiteSpace(contentType))
            throw new InvalidOperationException("ContentType nao informado no upload.");

        // Remove parametros (ex.: "image/jpeg; charset=binary" -> "image/jpeg")
        var mainType = contentType.Split(';', 2)[0].Trim();

        if (ContentSignatures.TryGetValue(mainType, out var matches) && !matches(content))
            throw new InvalidOperationException(
                $"Conteudo do arquivo nao corresponde ao tipo declarado '{mainType}'.");
    }

    // Mapa tipo -> verificador de assinatura de bytes. Tipos ausentes (ex.: text/csv)
    // nao tem assinatura confiavel e sao deliberadamente permitidos (default seguro = passa).
    private static readonly FrozenDictionary<string, Func<byte[], bool>> ContentSignatures =
        new Dictionary<string, Func<byte[], bool>>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = b => HasPrefix(b, 0, 0xFF, 0xD8, 0xFF),
            ["image/png"] = b => HasPrefix(b, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ["image/gif"] = b => HasPrefix(b, 0, 0x47, 0x49, 0x46, 0x38),                 // "GIF8"
            ["image/webp"] = b => HasPrefix(b, 0, 0x52, 0x49, 0x46, 0x46)                  // "RIFF"
                                   && HasPrefix(b, 8, 0x57, 0x45, 0x42, 0x50),              // "WEBP"
            ["application/pdf"] = b => HasPrefix(b, 0, 0x25, 0x50, 0x44, 0x46),             // "%PDF"
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = IsZip,
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = IsZip,
            ["application/vnd.ms-excel"] = b => HasPrefix(b, 0, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static bool HasPrefix(byte[] content, int offset, params byte[] signature)
    {
        if (content.Length < offset + signature.Length) return false;
        for (var i = 0; i < signature.Length; i++)
            if (content[offset + i] != signature[i]) return false;
        return true;
    }

    // OOXML (.xlsx/.docx) sao containers ZIP: "PK\x03\x04" (ou variantes vazio/spanned).
    private static bool IsZip(byte[] b) =>
        HasPrefix(b, 0, 0x50, 0x4B, 0x03, 0x04)
        || HasPrefix(b, 0, 0x50, 0x4B, 0x05, 0x06)
        || HasPrefix(b, 0, 0x50, 0x4B, 0x07, 0x08);
}
