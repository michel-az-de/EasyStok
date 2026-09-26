using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Passa a conversa para a dona (ferramenta <c>escalar_para_dona</c>, limite de iterações do
/// agente e ações de botão ainda sem handler automático). Não faz commit: quem chama confirma.
/// </summary>
public interface IEscaladorConversa
{
    Task EscalarAsync(Guid empresaId, Conversa conversa, string motivo, DateTime agora, CancellationToken ct = default);
}

/// <summary>
/// Mínimo da S06: <see cref="Conversa.Assumir"/> + <c>Mensagem(Sistema, motivo)</c>, que fica
/// interna (sem <c>wamid</c>, nunca enviada ao cliente).
/// TODO(S07): <c>EscalarConversaUseCase</c> substitui esta implementação (mesma interface) e
/// acrescenta, depois do commit, o evento de notificação <c>ConversaEscalada</c> (InApp + Web Push
/// para cada subscription da empresa) e o SSE <c>conversa.escalada</c>.
/// </summary>
public sealed class EscaladorConversa(IConversaRepository conversaRepository) : IEscaladorConversa
{
    public const string PrefixoMotivo = "escalado para a dona: ";

    public async Task EscalarAsync(Guid empresaId, Conversa conversa, string motivo, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversa);
        if (!conversa.EstaAberta) return;

        var texto = PrefixoMotivo + (string.IsNullOrWhiteSpace(motivo) ? "sem motivo informado" : motivo.Trim());
        if (texto.Length > Mensagem.TextoTamanhoMaximo) texto = texto[..Mensagem.TextoTamanhoMaximo];

        conversa.Assumir(agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto, texto),
            ct);
    }
}
