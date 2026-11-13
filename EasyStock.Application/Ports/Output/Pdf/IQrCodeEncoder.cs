namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>
/// Matriz do QR em ECC M e modo byte, o mesmo do <c>qrcode(0, 'M')</c> do PWA, para o número de módulos bater com o
/// que o resolvedor de layout calculou.
/// </summary>
public interface IQrCodeEncoder
{
    /// <returns>[linha, coluna], true = módulo escuro, sem a zona de silêncio.</returns>
    bool[,] Matriz(string conteudo);
}
