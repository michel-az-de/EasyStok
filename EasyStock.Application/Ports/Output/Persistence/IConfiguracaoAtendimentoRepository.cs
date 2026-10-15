using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IConfiguracaoAtendimentoRepository
{
    Task<ConfiguracaoAtendimento?> GetByEmpresaIdAsync(Guid empresaId);

    /// <summary>Devolve o padrão em memória (não persistido) quando a empresa não tem registro.</summary>
    Task<ConfiguracaoAtendimento> GetOrDefaultAsync(Guid empresaId);

    Task AddAsync(ConfiguracaoAtendimento configuracao);
    Task UpdateAsync(ConfiguracaoAtendimento configuracao);
}
