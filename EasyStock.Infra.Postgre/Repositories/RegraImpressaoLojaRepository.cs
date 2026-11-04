using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Npgsql;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Regra de impressão da loja (#2199). <c>configuracoes_loja</c> não tem <c>EmpresaId</c> nem RLS, então toda consulta e
/// toda gravação passam por <c>lojas</c> (filtro de tenant do EF e RLS do banco) com a empresa do token: loja de outra
/// empresa não é lida nem gravada.
/// </summary>
public sealed class RegraImpressaoLojaRepository(EasyStockDbContext db) : IRegraImpressaoLojaRepository
{
    public async Task<RegraImpressaoSalva?> ObterAsync(Guid empresaId, Guid lojaId, CancellationToken ct = default)
    {
        var loja = await db.Lojas.AsNoTracking()
            .Where(l => l.Id == lojaId && l.EmpresaId == empresaId)
            .Select(l => new
            {
                Config = db.ConfiguracoesLoja
                    .Where(c => c.LojaId == l.Id)
                    .Select(c => new { c.ImpressaoJson, c.ImpressaoVersao, c.ImpressaoAlteradaEm })
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        if (loja is null) return null;
        return loja.Config is null
            ? new RegraImpressaoSalva(null, 0, null)
            : new RegraImpressaoSalva(loja.Config.ImpressaoJson, loja.Config.ImpressaoVersao, loja.Config.ImpressaoAlteradaEm);
    }

    public async Task<GravacaoRegraImpressao> GravarAsync(
        Guid empresaId, Guid lojaId, int versaoEsperada, string jsonCanonico, DateTime agoraUtc, CancellationToken ct = default)
    {
        if (versaoEsperada == 0) await GarantirLinhaAsync(empresaId, lojaId, agoraUtc, ct);

        // Uma instrução só: o WHERE na versão é a trava. Duas gravações com a mesma versão esperada, uma vence.
        var afetadas = await DaLoja(empresaId, lojaId)
            .Where(c => c.ImpressaoVersao == versaoEsperada)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ImpressaoJson, jsonCanonico)
                .SetProperty(c => c.ImpressaoVersao, c => c.ImpressaoVersao + 1)
                .SetProperty(c => c.ImpressaoAlteradaEm, agoraUtc)
                .SetProperty(c => c.AlteradoEm, agoraUtc), ct);

        if (afetadas == 1) return new GravacaoRegraImpressao(true, versaoEsperada + 1);

        var atual = await ObterAsync(empresaId, lojaId, ct);
        return new GravacaoRegraImpressao(false, atual?.Versao ?? 0);
    }

    private IQueryable<ConfiguracaoLoja> DaLoja(Guid empresaId, Guid lojaId) =>
        db.ConfiguracoesLoja.Where(c =>
            c.LojaId == lojaId && db.Lojas.Any(l => l.Id == lojaId && l.EmpresaId == empresaId));

    /// <summary>
    /// Loja que nunca salvou configuração não tem linha. Cria a linha padrão (regra nula, versão 0) e deixa o UPDATE
    /// condicional decidir quem grava: se outra requisição criou a linha antes, o índice único recusa esta e segue.
    /// </summary>
    private async Task GarantirLinhaAsync(Guid empresaId, Guid lojaId, DateTime agoraUtc, CancellationToken ct)
    {
        if (await DaLoja(empresaId, lojaId).AnyAsync(ct)) return;
        if (!await db.Lojas.AnyAsync(l => l.Id == lojaId && l.EmpresaId == empresaId, ct)) return;

        var nova = ConfiguracaoLoja.CriarPadrao(lojaId);
        nova.CriadoEm = agoraUtc;
        nova.AlteradoEm = agoraUtc;
        db.ConfiguracoesLoja.Add(nova);
        try
        {
            // Com transação aberta, o EF salva dentro de um savepoint e desfaz só ele se o índice recusar.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Outra requisição criou a linha: o UPDATE condicional decide.
        }
        finally
        {
            db.Entry(nova).State = EntityState.Detached;
        }
    }
}
