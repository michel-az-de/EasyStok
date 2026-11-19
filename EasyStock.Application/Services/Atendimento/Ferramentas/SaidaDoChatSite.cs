using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// #2876: no chat do site não há Cloud API nem mídia por URL; o que no WhatsApp sai como imagem sai como texto, gravado como
/// saída do agente com id externo do canal (o widget lê pelo stream).
/// </summary>
internal static class SaidaDoChatSite
{
    public static bool Eh(Conversa conversa) => conversa.Canal == CanalConversa.ChatSite;

    public static async Task GravarTextoAsync(
        ContextoTurnoAgente contexto, IConversaRepository conversaRepository, string texto, CancellationToken ct)
    {
        var conversa = contexto.Conversa;
        if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                TipoConteudoMensagem.Texto, texto, CanalChatSite.PrefixoIdExterno + Guid.NewGuid().ToString("N")), ct);
        await contexto.GravarSaidaAsync(ct);
    }
}
