using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record ListarConversasAtendimentoQuery(
    Guid EmpresaId, SituacaoConversa? Situacao, string? Busca, int Pagina, int Limite, FiltroResponsavel? Responsavel = null);

/// <summary>Inbox do console (S07): mais recentes primeiro, com a última mensagem e as não lidas.</summary>
public sealed class ListarConversasAtendimentoUseCase(
    IConversaRepository conversaRepository, IConfiguracaoAtendimentoRepository configuracoes, IOptions<PrazosOptions> prazos)
{
    public const int LimitePadrao = 30;
    public const int LimiteMaximo = 100;

    public async Task<IReadOnlyList<ConversaResumoResult>> ExecuteAsync(
        ListarConversasAtendimentoQuery query, CancellationToken ct = default)
    {
        var limite = query.Limite <= 0 ? LimitePadrao : Math.Min(query.Limite, LimiteMaximo);
        var busca = string.IsNullOrWhiteSpace(query.Busca) ? null : query.Busca.Trim();

        var itens = await conversaRepository.ListarInboxAsync(
            query.EmpresaId, query.Situacao, busca, query.Responsavel, Math.Max(query.Pagina, 1), limite, ct);

        var agora = DateTime.UtcNow;
        var config = await configuracoes.GetByEmpresaIdAsync(query.EmpresaId);
        if (config is not null && config.EmpresaId != query.EmpresaId)
            throw new UseCaseValidationException("Configuração de outra empresa.");
        var sla = config?.SlaRespostaMinutos ?? prazos.Value.ClienteSemRespostaMin;
        return itens.Where(i => i.Conversa.EmpresaId == query.EmpresaId)
            .Select(i => ConversaResumoResult.De(i.Conversa, i.UltimaMensagemTexto, agora, sla, i.AguardaResposta)).ToList();
    }
}
