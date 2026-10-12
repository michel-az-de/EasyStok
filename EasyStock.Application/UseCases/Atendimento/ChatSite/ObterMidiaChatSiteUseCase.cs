using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.ChatSite;

/// <summary>
/// Foto que a loja mandou no chat do site, para o visitante (#1448). A credencial é o
/// <c>X-Chat-Token</c> da sessão: só sai mídia da conversa da própria sessão e só de mensagem que o
/// visitante já vê na lista (a dele ou a enviada pela loja pelo canal, com id externo). Nota interna,
/// mensagem de outra conversa, sem arquivo ou com o arquivo fora do storage: nulo (404 na borda).
/// </summary>
public sealed class ObterMidiaChatSiteUseCase(
    AcessoChatSite acesso, IConversaRepository conversaRepository, IFileStorage fileStorage)
{
    private const string MimePadrao = "application/octet-stream";

    public async Task<MidiaMensagemResult?> ExecuteAsync(string slug, string? token, Guid mensagemId, CancellationToken ct = default)
    {
        var sessao = await acesso.ResolverSessaoAsync(slug, token, DateTime.UtcNow, ct);
        if (sessao.ConversaId is not { } conversaId) return null;

        var mensagem = await conversaRepository.ObterMensagemAsync(sessao.EmpresaId, conversaId, mensagemId, ct);
        if (mensagem is null || (mensagem.Direcao != DirecaoMensagem.Entrada && mensagem.ExternoId is null)) return null;
        if (mensagem.MidiaChave is not { } chave || !await fileStorage.ExistsAsync(chave, ct)) return null;

        var conteudo = await fileStorage.DownloadAsync(chave, ct);
        return new MidiaMensagemResult(conteudo, string.IsNullOrWhiteSpace(mensagem.MidiaMime) ? MimePadrao : mensagem.MidiaMime);
    }
}
