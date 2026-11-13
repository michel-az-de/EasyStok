using EasyStock.Domain.ValueObjects;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// CLI-5 (#2321): o CPF/CNPJ que o app manda vira <c>Cliente.Documento</c> do ERP, normalizado como
/// nos casos de uso CriarCliente/AtualizarCliente (CNPJ valido so com digitos; o resto como veio).
/// </summary>
internal static class DocumentoClienteMobile
{
    private const int Maximo = 30;

    /// <summary>Null quando o app nao mandou documento: vazio nunca apaga o do ERP.</summary>
    internal static string? Normalizar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento)) return null;
        var limpo = documento.Trim();
        var valor = Cnpj.TryFrom(limpo)?.Value ?? limpo;
        return valor.Length > Maximo ? valor[..Maximo] : valor;
    }
}
