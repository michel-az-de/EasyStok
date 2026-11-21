using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>#2544: o WhatsApp só baixa mídia por HTTPS pública. Caminho gravado relativo ganha o endereço público da loja.</summary>
internal static class MidiaPublica
{
    public static string? Url(string? caminho, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        if (caminho.StartsWith('/'))
            caminho = (configuration["App:BaseUrl"]?.TrimEnd('/') ?? SaudacaoAtendimento.BaseUrlPadrao) + caminho;
        return Uri.TryCreate(caminho, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : null;
    }
}
