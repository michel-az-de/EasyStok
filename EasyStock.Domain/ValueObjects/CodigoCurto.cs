namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// #2018: o número curto do pedido, numa regra só: os 8 primeiros hexadecimais do id, em maiúsculas
/// (<c>A1B2C3D4</c>). É o que o canhoto, a cozinha, as entregas, o financeiro e os avisos mostram quando o pedido não
/// tem "Comanda N" nem número do dia. Também serve de referência curta para outros registros (caixa, impressão,
/// lembrete) nas mensagens para a equipe.
/// </summary>
public static class CodigoCurto
{
    /// <summary>Quantidade de caracteres do código.</summary>
    public const int Tamanho = 8;

    /// <summary>O código curto de <paramref name="id"/>, sempre em maiúsculas.</summary>
    public static string De(Guid id) => id.ToString("N")[..Tamanho].ToUpperInvariant();
}
