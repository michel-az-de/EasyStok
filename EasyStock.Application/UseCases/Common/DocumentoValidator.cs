using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Helper de validação de documento (CPF/CNPJ) dos use cases. Mapeado para HTTP 400 via
/// <see cref="UseCaseValidationException"/>.
///
/// <para>
/// Política "tem forma": valida o dígito verificador quando o documento tem forma de CPF
/// (11 dígitos) OU de CNPJ (14 dígitos). Documentos de outros comprimentos — passaporte
/// estrangeiro, dado legado — são <strong>tolerados</strong> (decisão documentada nos use
/// cases de Cliente/Fornecedor). No-op para vazio/null (documento é opcional).
/// </para>
/// </summary>
public static class DocumentoValidator
{
    /// <summary>
    /// Lança <see cref="UseCaseValidationException"/> quando o documento tem forma de CPF
    /// (11 dígitos) ou de CNPJ (14 dígitos) mas o dígito verificador não confere. Outros
    /// comprimentos passam (tolerância estrangeiro/legado).
    /// </summary>
    public static void EnsureValido(string? documento, string campoAmigavel = "Documento")
    {
        if (string.IsNullOrWhiteSpace(documento)) return;

        if (Cpf.TemFormaDeCpf(documento) && !Cpf.EhValido(documento))
            throw new UseCaseValidationException($"{campoAmigavel} inválido (CPF com dígito verificador incorreto).");

        if (Cnpj.TemFormaDeCnpj(documento) && !Cnpj.EhValido(documento))
            throw new UseCaseValidationException($"{campoAmigavel} inválido (CNPJ com dígito verificador incorreto).");
    }
}
