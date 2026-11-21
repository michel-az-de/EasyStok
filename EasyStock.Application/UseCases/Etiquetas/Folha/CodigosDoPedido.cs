using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>
/// Gera cada QR e cada CODE128 uma vez por conteúdo dentro do pedido (#2200): as cópias e as etiquetas iguais
/// reaproveitam a mesma matriz em vez de recalcular. Vive só durante um <see cref="GerarFolhaEtiquetasPdfUseCase.Planejar"/>.
/// </summary>
public sealed class CodigosDoPedido(IQrCodeEncoder qr)
{
    private readonly Dictionary<string, bool[,]> _qrs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _barras = new(StringComparer.Ordinal);

    /// <summary>Matriz do QR, compartilhada entre as etiquetas com o mesmo conteúdo; só leitura.</summary>
    public bool[,] Qr(string conteudo)
    {
        if (!_qrs.TryGetValue(conteudo, out var matriz)) _qrs[conteudo] = matriz = qr.Matriz(conteudo);
        return matriz;
    }

    /// <summary>Módulos do CODE128 (1 = barra, 0 = espaço) para um conteúdo já validado por <see cref="Code128.Problema"/>.</summary>
    public string Barras(string conteudo)
    {
        if (!_barras.TryGetValue(conteudo, out var modulos)) _barras[conteudo] = modulos = Code128.Codificar(conteudo).Modulos;
        return modulos;
    }
}
