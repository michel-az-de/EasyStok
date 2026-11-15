using System.Globalization;
using System.Text;

namespace EasyStock.Domain.ValueObjects.Pix;

/// <summary>
/// BR Code estático do Pix (#2233), padrão EMV do Banco Central: campos com identificador e tamanho de 2 dígitos e
/// CRC16-CCITT (polinômio 0x1021, semente 0xFFFF) no fim. Porte do <c>EasyStock.Console/src/infra/pix.js</c>, com a
/// chave da loja no lugar da de demonstração. Sem PSP: o pagamento é conferido por uma pessoa.
/// </summary>
public static class BrCodePix
{
    public const int TxidTamanhoMaximo = 25;
    private const string SemTxid = "***";

    /// <summary>
    /// Copia e cola do Pix para <paramref name="recebedor"/>. <paramref name="valor"/> nulo deixa o valor para o
    /// pagador; <paramref name="txid"/> nulo vira <c>***</c>, como o manual do Banco Central define.
    /// </summary>
    public static string Estatico(RecebedorPix recebedor, decimal? valor, string? txid)
    {
        ArgumentNullException.ThrowIfNull(recebedor);
        if (valor is <= 0m) throw new RegraDeDominioVioladaException("Valor do Pix deve ser maior que zero.");
        if (txid is not null && (txid.Length is 0 or > TxidTamanhoMaximo || !txid.All(char.IsAsciiLetterOrDigit)))
            throw new RegraDeDominioVioladaException($"Identificador do Pix deve ter de 1 a {TxidTamanhoMaximo} letras ou números.");

        var semCrc = new StringBuilder()
            .Append(Campo("00", "01"))
            .Append(Campo("26", Campo("00", "br.gov.bcb.pix") + Campo("01", recebedor.Chave.Valor)))
            .Append(Campo("52", "0000"))
            .Append(Campo("53", "986"));
        if (valor is { } v) semCrc.Append(Campo("54", v.ToString("0.00", CultureInfo.InvariantCulture)));
        semCrc.Append(Campo("58", "BR"))
            .Append(Campo("59", recebedor.Nome))
            .Append(Campo("60", recebedor.Cidade))
            .Append(Campo("62", Campo("05", txid ?? SemTxid)))
            .Append("6304");

        var texto = semCrc.ToString();
        return texto + Crc16(texto);
    }

    /// <summary>Identificador do Pix do pedido: o código curto que a loja vê no console, para achar o Pix no extrato.</summary>
    public static string TxidDoPedido(Guid pedidoId) => "PED" + CodigoCurto.De(pedidoId);

    /// <summary>CRC16-CCITT-FALSE em 4 dígitos hexadecimais maiúsculos.</summary>
    public static string Crc16(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var crc = 0xFFFF;
        foreach (var c in texto)
        {
            crc ^= c << 8;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) == 0 ? (crc << 1) & 0xFFFF : ((crc << 1) ^ 0x1021) & 0xFFFF;
        }
        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static string Campo(string id, string valor)
    {
        if (valor.Length > 99) throw new RegraDeDominioVioladaException($"Campo {id} do Pix acima de 99 caracteres.");
        return id + valor.Length.ToString("00", CultureInfo.InvariantCulture) + valor;
    }
}
