using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>Desenha o canhoto da comanda como arquivo (#2211): PNG para o WhatsApp, PDF para quem quiser o documento.</summary>
public interface IRenderizadorCanhoto
{
    Task<AnexoGerado> RenderizarAsync(CanhotoDto canhoto, FormatoAnexo formato, CancellationToken ct = default);
}
