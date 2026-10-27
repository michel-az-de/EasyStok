using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IConfiguracaoAtendimentoRepository
{
    Task<ConfiguracaoAtendimento?> GetByEmpresaIdAsync(Guid empresaId);

    /// <summary>Devolve o padrão em memória (não persistido) quando a empresa não tem registro.</summary>
    Task<ConfiguracaoAtendimento> GetOrDefaultAsync(Guid empresaId);

    /// <summary>
    /// #2096: só os avisos da operação, sem depender do tenant do contexto (jobs entre empresas). Sem registro, o padrão.
    /// Uso do servidor: a empresa vem do produtor, nunca da requisição.
    /// </summary>
    Task<AvisosOperacao> ObterAvisosAsync(Guid empresaId, CancellationToken ct = default);

    Task AddAsync(ConfiguracaoAtendimento configuracao);
    Task UpdateAsync(ConfiguracaoAtendimento configuracao);
}
