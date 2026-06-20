using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Helper de validação de documento (CPF/CNPJ) dos use cases. Mapeado para HTTP 400 via
/// <see cref="UseCaseValidationException"/>.
///
/// <para>
/// Política "tem forma": valida o dígito verificador SÓ quando o documento tem forma de CPF
/// (11 dígitos). Documentos de outros comprimentos — CNPJ (14), passaporte estrangeiro, dado
/// legado — são <strong>tolerados</strong> (decisão documentada em <c>CriarClienteUseCase</c>:
/// "CPF/CNPJ só dígitos quando válido; senão preserva input"). No-op para vazio/null.
/// </para>
/// </summary>
public static class DocumentoValidator
{
    /// <summary>
    /// Lança <see cref="UseCaseValidationException"/> quando o documento tem forma de CPF
    /// (11 dígitos) mas o dígito verificador não confere. Outros formatos passam.
    /// </summary>
    public static void EnsureValido(string? documento, string campoAmigavel = "Documento")
    {
        if (string.IsNullOrWhiteSpace(documento)) return;
        if (Cpf.TemFormaDeCpf(documento) && !Cpf.EhValido(documento))
            throw new UseCaseValidationException($"{campoAmigavel} inválido (CPF com dígito verificador incorreto).");
    }
}
