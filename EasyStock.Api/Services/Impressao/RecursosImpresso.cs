using System.Net;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Services.Impressao;

public static class RecursosImpresso
{
    public static async Task<string> CongelarAsync(string html, Guid empresa, string origem, IWebHostEnvironment ambiente, IFileStorage storage, CancellationToken ct = default)
    {
        foreach (var fonte in new[] { "Lora.ttf", "NunitoSans.ttf" })
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(ambiente.WebRootPath, "impressao", "fontes", fonte), ct);
            html = html.Replace(origem + ImpressoHtml.CaminhoFontes + "/" + fonte,
                "data:font/ttf;base64," + Convert.ToBase64String(bytes), StringComparison.Ordinal);
        }
        foreach (Match imagem in Regex.Matches(html, "<img[^>]+src=\"([^\"]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var url = WebUtility.HtmlDecode(imagem.Groups[1].Value);
            // Só o storage próprio, com chave do tenant. Nunca buscar URL arbitrária na rede.
            var prefixo = $"lojas/{empresa}/";
            var caminho = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
            var inicio = caminho.IndexOf(prefixo, StringComparison.Ordinal);
            var chave = inicio < 0 ? null : caminho[inicio..];
            var partes = chave?.Split('/');
            if (partes is not { Length: 5 } || !Guid.TryParse(partes[2], out var loja) || partes[3] != "logo"
                || partes[4].Contains("..", StringComparison.Ordinal))
                throw new UseCaseValidationException("Atualize o logo em Configurações, Impressão para guardar este documento no histórico.");
            var bytes = await storage.DownloadAsync(chave!, ct);
            if (bytes.Length > 5 * 1024 * 1024) throw new UseCaseValidationException("Logo maior que 5 MB.");
            var mime = Path.GetExtension(chave!).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => null };
            if (mime is null) throw new UseCaseValidationException("Use um logo PNG ou JPG.");
            html = html.Replace(imagem.Groups[1].Value, $"data:{mime};base64,{Convert.ToBase64String(bytes)}", StringComparison.Ordinal);
        }
        return html;
    }
}

