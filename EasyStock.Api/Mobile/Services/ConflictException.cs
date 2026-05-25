namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Lançada quando uma mutation chega com timestamp anterior à versão do servidor.
/// <see cref="WinningPayload"/> traz a versao server vencedora (DTO serializado)
/// pra que o PWA exiba diff visual ao operador.
/// </summary>
public class ConflictException(string message, System.Text.Json.JsonElement? winningPayload = null) : Exception(message)
{
    public System.Text.Json.JsonElement? WinningPayload { get; } = winningPayload;
}
