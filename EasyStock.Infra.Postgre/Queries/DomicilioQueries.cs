using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Queries;

/// <summary>
/// Sinal de mesmo domicílio (S25, RN-13, D10): outro cliente ativo da empresa com endereço de mesma
/// <see cref="ClienteEndereco.ChaveDomicilio"/>. Só id e nome; nada do histórico do outro cadastro.
/// O banco pré-filtra pelo número (sem normalizar CEP em SQL); a chave completa é comparada aqui.
/// <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// </summary>
public sealed class DomicilioQueries(EasyStockDbContext db) : IDomicilioQueries
{
    public async Task<IReadOnlyList<ClienteMesmoDomicilio>> ListarMesmoDomicilioAsync(
        Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        var proprios = await db.Set<ClienteEndereco>()
            .AsNoTracking()
            .Where(e => e.ClienteId == clienteId && e.Cliente!.EmpresaId == empresaId)
            .Select(e => new { e.Cep, e.Numero, e.Complemento })
            .ToListAsync(ct);

        var chaves = proprios
            .Select(e => ClienteEndereco.ChaveDomicilio(e.Cep, e.Numero, e.Complemento))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (chaves.Count == 0) return [];

        var numeros = proprios
            .Where(e => ClienteEndereco.ChaveDomicilio(e.Cep, e.Numero, e.Complemento) is not null)
            .Select(e => e.Numero!.Trim().ToLower())
            .Distinct()
            .ToList();

        var candidatos = await db.Set<ClienteEndereco>()
            .AsNoTracking()
            .Where(e => e.ClienteId != clienteId
                && e.Cliente!.EmpresaId == empresaId
                && e.Cliente.Ativo
                && e.Numero != null
                && numeros.Contains(e.Numero.Trim().ToLower()))
            .Select(e => new { e.ClienteId, e.Cliente!.Nome, e.Cep, e.Numero, e.Complemento })
            .ToListAsync(ct);

        return candidatos
            .Where(c => ClienteEndereco.ChaveDomicilio(c.Cep, c.Numero, c.Complemento) is { } chave && chaves.Contains(chave))
            .GroupBy(c => c.ClienteId)
            .Select(g => new ClienteMesmoDomicilio(g.Key, g.First().Nome))
            .OrderBy(c => c.Nome, StringComparer.CurrentCulture)
            .ToList();
    }
}
