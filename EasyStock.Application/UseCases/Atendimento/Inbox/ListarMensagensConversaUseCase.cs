using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record ListarMensagensConversaQuery(Guid EmpresaId, Guid ConversaId, DateTime? AntesDe, int Limite);

/// <summary>Histórico da conversa para o console (S07), paginado para trás pelo cursor <c>antesDe</c>.</summary>
public sealed class ListarMensagensConversaUseCase(IConversaRepository conversaRepository)
{
    public const int LimitePadrao = 50;
    public const int LimiteMaximo = 200;

    public async Task<IReadOnlyList<MensagemAtendimentoResult>> ExecuteAsync(
        ListarMensagensConversaQuery query, CancellationToken ct = default)
    {
        _ = await conversaRepository.ObterPorIdAsync(query.EmpresaId, query.ConversaId, ct)
            ?? throw new ConversaNaoEncontradaException(query.ConversaId);

        var limite = query.Limite <= 0 ? LimitePadrao : Math.Min(query.Limite, LimiteMaximo);
        var mensagens = await conversaRepository.ListarMensagensAsync(
            query.EmpresaId, query.ConversaId, Utc(query.AntesDe), limite, ct);

        return mensagens.Select(MensagemAtendimentoResult.De).ToList();
    }

    private static DateTime? Utc(DateTime? d) => d switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };
}
