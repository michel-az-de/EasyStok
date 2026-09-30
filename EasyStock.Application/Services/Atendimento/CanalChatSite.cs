using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Canal do chat do site (S36). Não há provedor externo: a mensagem de saída gravada pelo use case é o
/// que o visitante recebe, lida do banco pelo stream da sessão. O adaptador só devolve um id externo,
/// e é esse id que separa o que foi enviado ao visitante das notas internas do sistema (sem id).
/// </summary>
public sealed class CanalChatSite : ICanalMensageria
{
    public const string PrefixoIdExterno = "chatsite:";

    public CanalConversa Canal => CanalConversa.ChatSite;

    public Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default) =>
        Task.FromResult(PrefixoIdExterno + Guid.NewGuid().ToString("N"));

    public Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        throw new NotSupportedException("O chat do site só aceita texto.");

    public Task<string> EnviarBotoesAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        throw new NotSupportedException("O chat do site só aceita texto.");

    public Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        throw new NotSupportedException("O chat do site não tem modelo: não há janela.");

    /// <summary>Leitura é do próprio console; nada a avisar a um provedor.</summary>
    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) => Task.CompletedTask;
}
