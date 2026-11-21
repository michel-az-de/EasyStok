using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications.Anexos;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>Documento pronto para sair no WhatsApp: URL HTTPS pública e o nome que a cliente vê.</summary>
public sealed record DocumentoPublicado(string Url, string NomeArquivo);

/// <summary>
/// #2962 (E3): gera o PDF do pedido (ou o comprovante) e sobe com nome aleatório, como o canhoto (#2544), para o WhatsApp
/// baixar pela URL pública. O nome aleatório é a proteção: a chave do arquivo não se adivinha.
/// </summary>
public sealed class PublicadorDocumentoPedido(
    IRenderizadorPdfPedido renderizador, IFileStorage armazenamento, IConfiguration configuration)
{
    public const string ChaveEnviarPdf = "Atendimento:Cobranca:EnviarPdf";

    /// <summary>O envio de PDF vem ligado; <c>Atendimento:Cobranca:EnviarPdf=false</c> volta ao texto e à imagem de antes.</summary>
    public static bool PdfLigado(IConfiguration configuration) =>
        !bool.TryParse(configuration[ChaveEnviarPdf], out var ligado) || ligado;

    public static string NomeDoPedido(string codigo) => $"Pedido-{codigo}.pdf";

    public static string NomeDoComprovante(string codigo) => $"Comprovante-{codigo}.pdf";

    public async Task<DocumentoPublicado> PublicarPedidoAsync(
        Guid empresaId, ResumoPedidoDto resumo, PixPdfDto pix, CancellationToken ct)
    {
        var pdf = await renderizador.RenderizarPedidoAsync(resumo, pix, ct);
        return await SubirAsync(empresaId, pdf, NomeDoPedido(resumo.Codigo), ct);
    }

    public async Task<DocumentoPublicado> PublicarComprovanteAsync(
        Guid empresaId, ResumoPedidoDto resumo, DateTime? pagoEm, CancellationToken ct)
    {
        var pdf = await renderizador.RenderizarComprovanteAsync(resumo, pagoEm, ct);
        return await SubirAsync(empresaId, pdf, NomeDoComprovante(resumo.Codigo), ct);
    }

    private async Task<DocumentoPublicado> SubirAsync(Guid empresaId, AnexoGerado pdf, string nomeArquivo, CancellationToken ct)
    {
        // Como o canhoto (#2544): nome aleatório em atendimento/*, URL pública porque o WhatsApp baixa por ela.
        // O link assinado do armazenamento local (/api/files/local-signed/...) não existe na API e dava 404.
        var salvo = await armazenamento.UploadAsync(new FileUploadRequest(
            $"atendimento/documentos/{empresaId:N}", $"{Guid.NewGuid():D}.pdf", pdf.ContentType, pdf.Conteudo), ct);
        var url = MidiaPublica.Url(salvo.Url, configuration)
            ?? throw new InvalidOperationException("O PDF não ficou em link HTTPS público.");
        return new DocumentoPublicado(url, nomeArquivo);
    }
}
