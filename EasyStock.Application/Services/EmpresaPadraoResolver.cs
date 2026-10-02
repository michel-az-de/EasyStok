using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services;

/// <summary>Memória do processo para o <see cref="EmpresaPadraoResolver"/> (singleton): só guarda acerto, nunca falha.</summary>
public sealed class EmpresaPadraoCache
{
    private readonly ConcurrentDictionary<string, Guid> _porChave = new(StringComparer.Ordinal);

    internal bool TryGet(string chave, out Guid empresaId) => _porChave.TryGetValue(chave, out empresaId);

    internal void Guardar(string chave, Guid empresaId) => _porChave[chave] = empresaId;
}

/// <summary>
/// Resolve <c>Auth:Google:EmpresaPadrao</c> (#1326, extraído do <c>AuthController</c> na N13): o CNPJ primeiro e, sem
/// casamento, o nome exato quando uma só empresa o tem. Lê a configuração do host (a mesma na API e no Worker) e o
/// <see cref="IEmpresaRepository"/>, que o login anônimo já chama sem tenant. Resolve uma vez por processo e não guarda
/// a falha, para a empresa criada depois ser achada sem reiniciar.
/// </summary>
public sealed class EmpresaPadraoResolver(
    IConfiguration configuration,
    IEmpresaRepository empresas,
    EmpresaPadraoCache cache,
    ILogger<EmpresaPadraoResolver> logger) : IEmpresaPadraoResolver
{
    /// <summary>Chave de configuração da empresa padrão (CNPJ ou nome exato).</summary>
    public const string Chave = "Auth:Google:EmpresaPadrao";

    public async Task<Guid?> ResolverAsync(CancellationToken ct = default)
    {
        var chave = configuration[Chave]?.Trim();
        if (string.IsNullOrEmpty(chave))
        {
            logger.LogWarning("{Chave} não configurada: não há empresa padrão para os avisos de plataforma.", Chave);
            return null;
        }

        if (cache.TryGet(chave, out var guardada)) return guardada;

        var empresa = await empresas.GetByDocumentoAsync(chave);
        if (empresa is null)
        {
            var porNome = (await empresas.GetAllAsync())
                .Where(e => string.Equals(e.Nome?.Trim(), chave, StringComparison.OrdinalIgnoreCase))
                .ToList();
            empresa = porNome.Count == 1 ? porNome[0] : null;
        }

        if (empresa is null)
        {
            logger.LogWarning("{Chave} '{Valor}' não corresponde a uma única empresa.", Chave, chave);
            return null;
        }

        cache.Guardar(chave, empresa.Id);
        return empresa.Id;
    }
}
