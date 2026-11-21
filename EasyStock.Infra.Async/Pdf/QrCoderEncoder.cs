using EasyStock.Application.Ports.Output.Pdf;
using QRCoder;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// QR em ECC M forçando o modo byte (UTF-8), como o <c>qrcode(0, 'M')</c> do PWA: sem isso o QRCoder escolhe o modo
/// alfanumérico para textos em maiúsculas e gera um símbolo menor que o resolvedor reservou.
/// </summary>
public sealed class QrCoderEncoder : IQrCodeEncoder
{
    /// <summary>Borda que o QRCoder sempre acrescenta à <c>ModuleMatrix</c>.</summary>
    private const int BordaQrCoder = 4;

    public bool[,] Matriz(string conteudo)
    {
        ArgumentException.ThrowIfNullOrEmpty(conteudo);
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(conteudo, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        var linhas = dados.ModuleMatrix;
        var n = linhas.Count - 2 * BordaQrCoder;
        var matriz = new bool[n, n];
        for (var linha = 0; linha < n; linha++)
            for (var coluna = 0; coluna < n; coluna++)
                matriz[linha, coluna] = linhas[linha + BordaQrCoder][coluna + BordaQrCoder];
        return matriz;
    }
}
