using System.Text;
using QRCoder;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// QR code em SVG para os impressos (#1632): um <c>path</c> preto com módulos inteiros. A matriz do QRCoder já
/// traz a zona de silêncio de 4 módulos, então o <c>viewBox</c> é a matriz inteira e o CSS dá o tamanho em
/// múltiplos do ponto da térmica.
/// </summary>
public static class QrSvg
{
    public static string Gerar(string conteudo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conteudo);

        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(conteudo, QRCodeGenerator.ECCLevel.M);
        var matriz = dados.ModuleMatrix;
        var lado = matriz.Count;

        var d = new StringBuilder();
        for (var linha = 0; linha < lado; linha++)
            for (var coluna = 0; coluna < lado; coluna++)
                if (matriz[linha][coluna])
                    d.Append('M').Append(coluna).Append(' ').Append(linha).Append("h1v1h-1z");

        return $"<svg class=\"qr\" viewBox=\"0 0 {lado} {lado}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR code\">"
             + $"<path fill=\"#000\" d=\"{d}\"/></svg>";
    }
}
