using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// #3017 (E3): o impresso HTML vira PNG pelo Gotenberg (Chromium) na rede interna <c>render</c>. Só guarda o que o
/// Chromium precisa: nunca loga o HTML, o código Pix ou o corpo da resposta. Falha com exceção; quem chama cai no texto.
/// </summary>
public sealed class GotenbergRenderizadorHtml(HttpClient http, IConfiguration configuracao) : IRenderizadorHtml
{
    public const string ChaveUrl = "Impressao:Renderizador:Url";

    private const string Caminho = "/forms/chromium/screenshot/html";
    private const string SeletorImpresso = ".im";
    private const string EsperaDasFontes = "document.fonts.status==='loaded'";
    private const double PixelsPorMm = 96d / 25.4d;
    private static readonly byte[] AssinaturaPng = [0x89, 0x50, 0x4E, 0x47];

    public async Task<AnexoGerado> RenderizarPngAsync(DocumentoHtml documento, int larguraMm, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var url = configuracao[ChaveUrl]?.Trim().TrimEnd('/')
            ?? throw new InvalidOperationException($"{ChaveUrl} não configurada.");

        using var form = new MultipartFormDataContent();
        form.Add(Texto(((int)Math.Round(larguraMm * PixelsPorMm)).ToString(CultureInfo.InvariantCulture)), "width");
        form.Add(Texto("png"), "format");
        form.Add(Texto(SeletorImpresso), "selector");
        form.Add(Texto("3"), "deviceScaleFactor");
        form.Add(Texto(EsperaDasFontes), "waitForExpression");
        form.Add(Texto("true"), "failOnResourceLoadingFailed");
        form.Add(Arquivo(Encoding.UTF8.GetBytes(documento.Html), "text/html"), "files", "index.html");
        foreach (var recurso in documento.Recursos)
            form.Add(Arquivo(recurso.Conteudo, recurso.ContentType), "files", recurso.Caminho);

        HttpResponseMessage resposta;
        try
        {
            resposta = await http.PostAsync(url + Caminho, form, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // O timeout do HttpClient é um OperationCanceledException: sem converter, o fallback do balão 1 não roda.
            throw new TimeoutException("O Gotenberg não respondeu a tempo.");
        }

        using (resposta)
        {
            if (!resposta.IsSuccessStatusCode)
                throw new HttpRequestException($"O Gotenberg respondeu {(int)resposta.StatusCode}.");
            var png = await resposta.Content.ReadAsByteArrayAsync(ct);
            if (!png.AsSpan().StartsWith(AssinaturaPng))
                throw new InvalidDataException("O Gotenberg não devolveu um PNG.");
            return new AnexoGerado(png, "image/png", "png");
        }
    }

    private static StringContent Texto(string valor) => new(valor, Encoding.UTF8);

    private static ByteArrayContent Arquivo(byte[] conteudo, string tipo)
    {
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        return arquivo;
    }
}
