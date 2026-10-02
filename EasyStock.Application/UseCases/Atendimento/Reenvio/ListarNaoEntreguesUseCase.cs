using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Reenvio;

/// <summary>Linha do painel "Não entregues" do console (S59): a mensagem e quem devia recebê-la.</summary>
public sealed record MensagemNaoEntregueResult(
    Guid ConversaId,
    string? ContatoNome,
    string ContatoIdExterno,
    CanalConversa Canal,
    bool ConversaAberta,
    MensagemAtendimentoResult Mensagem);

/// <summary>
/// S59 (#1391): mensagens de saída que falharam na empresa, mais recentes primeiro. Inclui as que esperam o cliente
/// responder ao modelo de retomada e as que saíram pela reserva por SMS (o WhatsApp não entregou).
/// </summary>
public sealed class ListarNaoEntreguesUseCase(IConversaRepository conversaRepository)
{
    public const int LimitePadrao = 50;
    public const int LimiteMaximo = 200;

    public async Task<IReadOnlyList<MensagemNaoEntregueResult>> ExecuteAsync(Guid empresaId, int? limite, CancellationToken ct = default)
    {
        var linhas = await conversaRepository.ListarNaoEntreguesAsync(
            empresaId, Math.Clamp(limite ?? LimitePadrao, 1, LimiteMaximo), ct);
        return linhas.Select(l => new MensagemNaoEntregueResult(
            l.Conversa.Id, l.Conversa.ContatoNome, l.Conversa.ContatoIdExterno, l.Conversa.Canal, l.Conversa.EstaAberta,
            MensagemAtendimentoResult.De(l.Mensagem))).ToList();
    }
}
