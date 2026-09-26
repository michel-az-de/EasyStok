using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Porta de envio de um canal de atendimento (S34, ADR-0051). Cada canal tem um adaptador; o que o
/// canal aceita (janela, modelo, mídia, botões) é regra de domínio em
/// <see cref="Domain.ValueObjects.CapacidadesCanal"/> e é conferido antes de chamar a porta.
/// Todo envio devolve o id externo da mensagem no canal (ex.: <c>wamid</c>).
/// </summary>
public interface ICanalMensageria
{
    CanalConversa Canal { get; }

    Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default);

    Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default);

    Task<string> EnviarBotoesAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default);

    /// <summary>Modelo aprovado (fora da janela). Só em canal com <c>AceitaModelo</c>.</summary>
    Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default);

    Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default);
}
