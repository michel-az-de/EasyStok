using EasyStock.Domain.Services;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>Um passo marcado da configuração inicial, com o nome de quem marcou (#2070).</summary>
public sealed record PassoMarcado(string Passo, SituacaoDoPasso Situacao, DateTime Em, Guid PorUsuarioId, string? PorNome);

/// <summary>Marcas dos passos da configuração inicial (#2070), uma linha por empresa e passo.</summary>
public interface IConfiguracaoInicialRepository
{
    /// <summary>Os passos marcados da empresa, com <c>EmpresaId</c> no WHERE além do RLS.</summary>
    Task<IReadOnlyList<PassoMarcado>> ListarPassosAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>
    /// Grava a marca por upsert atômico (<c>ON CONFLICT (EmpresaId, Passo)</c>): dois aparelhos marcando passos
    /// diferentes gravam os dois, e o mesmo passo nunca duplica. Imediato, sem esperar o <c>CommitAsync</c>.
    /// </summary>
    Task MarcarAsync(PassoConfiguracaoInicial passo, CancellationToken ct = default);
}

/// <summary>Contagens baratas que alimentam a prontidão dos passos essenciais (#2070), com <c>EmpresaId</c> explícito.</summary>
public interface IRetratoDaConfiguracaoInicialQuery
{
    Task<RetratoDaConfiguracaoInicial> ObterAsync(Guid empresaId, CancellationToken ct = default);
}
