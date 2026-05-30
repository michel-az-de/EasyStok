namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistência das configurações globais do sistema (back-office Admin).
/// Encapsula a semeadura de defaults + update-or-create + leitura que antes
/// viviam direto no AdminConfiguracoesController (F7).
/// </summary>
public interface IConfiguracaoSistemaRepository
{
    Task<IReadOnlyList<ConfiguracaoSistemaItem>> ListarTodasAsync(CancellationToken ct = default);

    /// <summary>Retorna apenas as chaves públicas presentes (Chave → Valor).</summary>
    Task<IReadOnlyDictionary<string, string>> ObterPublicasAsync(
        IReadOnlyCollection<string> chavesPublicas, CancellationToken ct = default);

    /// <summary>Semeia as chaves default ainda inexistentes. Só persiste se houver faltantes.</summary>
    Task GarantirDefaultsAsync(
        IReadOnlyCollection<ConfiguracaoDefault> defaults, CancellationToken ct = default);

    /// <summary>Update-or-create de cada item e persiste. Retorna a quantidade processada.</summary>
    Task<int> AplicarPatchAsync(
        IReadOnlyCollection<ConfiguracaoPatchItem> itens, string alteradoPor, CancellationToken ct = default);
}

public sealed record ConfiguracaoSistemaItem(
    string Chave, string Valor, string Descricao, DateTime AlteradoEm, string AlteradoPor);

public sealed record ConfiguracaoDefault(string Chave, string Valor, string Descricao);

public sealed record ConfiguracaoPatchItem(string Chave, string Valor);
