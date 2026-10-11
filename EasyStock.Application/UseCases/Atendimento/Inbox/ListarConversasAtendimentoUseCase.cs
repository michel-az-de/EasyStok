using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record ListarConversasAtendimentoQuery(
    Guid EmpresaId, SituacaoConversa? Situacao, string? Busca, int Pagina, int Limite, FiltroResponsavel? Responsavel = null);

/// <summary>Inbox do console (S07): mais recentes primeiro, com a última mensagem e as não lidas.</summary>
public sealed class ListarConversasAtendimentoUseCase(
    IConversaRepository conversaRepository, IConfiguracaoAtendimentoRepository configuracoes, IOptions<PrazosOptions> prazos,
    ICacheService? cacheService = null, ILogger<ListarConversasAtendimentoUseCase>? logger = null)
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
        var sla = await SlaRespostaAsync(query.EmpresaId);
        return itens.Where(i => i.Conversa.EmpresaId == query.EmpresaId)
            .Select(i => ConversaResumoResult.De(i.Conversa, i.UltimaMensagemTexto, agora, sla, i.AguardaResposta, i.AssumidaPorNome)).ToList();
    }

    /// <summary>
    /// #1566: a inbox faz polling a cada 5 s; o SLA fica em cache por empresa (<see cref="CacheKeys.AtendimentoSlaResposta"/>)
    /// e salvar a configuração apaga a chave. Cache fora do ar vira miss: a inbox não depende dele.
    /// </summary>
    private async Task<int> SlaRespostaAsync(Guid empresaId)
    {
        var chave = CacheKeys.AtendimentoSlaResposta(empresaId);
        if (await LerDoCacheAsync(chave) is { } emCache) return emCache;

        var config = await configuracoes.GetByEmpresaIdAsync(empresaId);
        if (config is not null && config.EmpresaId != empresaId)
            throw new UseCaseValidationException("Configuração de outra empresa.");
        var sla = config?.SlaRespostaMinutos ?? prazos.Value.ClienteSemRespostaMin;

        if (cacheService is not null)
        {
            try { await cacheService.SetAsync(chave, sla, CacheKeys.AtendimentoSlaRespostaTtl); }
            catch (Exception ex) { logger?.LogWarning(ex, "Inbox do atendimento: cache indisponivel na gravacao do SLA"); }
        }
        return sla;
    }

    private async Task<int?> LerDoCacheAsync(string chave)
    {
        if (cacheService is null) return null;
        try { return await cacheService.GetAsync<int?>(chave); }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Inbox do atendimento: cache indisponivel na leitura do SLA, seguindo pelo banco");
            return null;
        }
    }
}
