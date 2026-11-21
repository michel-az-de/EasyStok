namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// #2833: canal que reage e apaga para todos uma mensagem já enviada. Interface à parte de
/// <see cref="ICanalMensageria"/>, como <see cref="ICanalComAudio"/>, para não mudar os adaptadores que não oferecem.
/// Ação que a conexão ligada não oferece lança <see cref="AcaoNaoSuportadaWhatsAppException"/> sem enviar nada.
/// </summary>
public interface ICanalComAcoesNaMensagem
{
    /// <summary><paramref name="emoji"/> nulo retira a reação.</summary>
    Task ReagirAsync(string contatoIdExterno, string idMensagemExterna, string? emoji, CancellationToken ct = default);

    Task ApagarParaTodosAsync(string contatoIdExterno, string idMensagemExterna, CancellationToken ct = default);
}
