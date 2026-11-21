using EasyStock.Application.Services.Notifications.Anexos;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>#3017 (E3): HTML do impresso para PNG, na largura do papel. A imagem é o mesmo impresso que a cliente recebe.</summary>
public interface IRenderizadorHtml
{
    /// <param name="documento">HTML já pronto, com as fontes referenciadas por caminho relativo e enviadas junto.</param>
    /// <param name="larguraMm">Largura do papel (80 para a bobina). Sai em PNG com a largura exata.</param>
    Task<AnexoGerado> RenderizarPngAsync(DocumentoHtml documento, int larguraMm, CancellationToken ct = default);
}

/// <summary>HTML e os arquivos que ele referencia por caminho relativo (fontes, imagens).</summary>
public sealed record DocumentoHtml(string Html, IReadOnlyList<RecursoHtml> Recursos);

/// <param name="Caminho">Caminho relativo ao HTML, com barras (ex.: <c>impressao/fontes/Lora.ttf</c>).</param>
public sealed record RecursoHtml(string Caminho, string ContentType, byte[] Conteudo);
