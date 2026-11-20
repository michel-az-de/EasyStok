using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications.Anexos;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>Documento pronto para sair no WhatsApp: link assinado que expira e o nome que a cliente vê.</summary>
public sealed record DocumentoPublicado(string Url, string NomeArquivo);

/// <summary>
/// #2962 (E3): gera o PDF do pedido (ou o comprovante), grava no armazenamento PRIVADO com nome aleatório e devolve o
/// link assinado. O PDF leva a chave Pix da loja: o arquivo nunca é público e o link expira
/// (<c>Atendimento:Cobranca:ValidadeLinkHoras</c>, padrão 24 h).
/// </summary>
public sealed class PublicadorDocumentoPedido(
    IRenderizadorPdfPedido renderizador, IFileStorage armazenamento, IConfiguration configuration)
{
    public const string ChaveEnviarPdf = "Atendimento:Cobranca:EnviarPdf";
    public const string ChaveValidadeHoras = "Atendimento:Cobranca:ValidadeLinkHoras";
    public const int ValidadePadraoHoras = 24;

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
        // Nome aleatório, como o canhoto: a chave do arquivo não se adivinha. Privado: só o link assinado abre.
        var salvo = await armazenamento.UploadAsync(new FileUploadRequest(
            $"atendimento/documentos/{empresaId:N}", $"{Guid.NewGuid():N}.pdf", pdf.ContentType, pdf.Conteudo, IsPublic: false), ct);
        var assinada = await armazenamento.CreatePreSignedDownloadUrlAsync(salvo.StorageKey, Validade(), nomeArquivo, ct);
        var url = MidiaPublica.Url(assinada.ToString(), configuration)
            ?? throw new InvalidOperationException("O PDF não ficou em link HTTPS público.");
        return new DocumentoPublicado(url, nomeArquivo);
    }

    private TimeSpan Validade() =>
        TimeSpan.FromHours(int.TryParse(configuration[ChaveValidadeHoras], out var horas) && horas > 0 ? horas : ValidadePadraoHoras);
}
