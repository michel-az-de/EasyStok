namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Regra de impressão da loja (#2199), guardada em <c>configuracoes_loja</c>. Todo acesso passa pela loja da empresa
/// (filtro de tenant e RLS de <c>lojas</c>): loja de outra empresa responde como inexistente.
/// </summary>
public interface IRegraImpressaoLojaRepository
{
    /// <summary><c>null</c> quando a loja não existe ou não é da empresa.</summary>
    Task<RegraImpressaoSalva?> ObterAsync(Guid empresaId, Guid lojaId, CancellationToken ct = default);

    /// <summary>
    /// Grava se, e só se, a versão no banco ainda for <paramref name="versaoEsperada"/>, numa única instrução. Sem linha
    /// de configuração, a versão é 0 e a gravação cria a linha.
    /// </summary>
    Task<GravacaoRegraImpressao> GravarAsync(
        Guid empresaId, Guid lojaId, int versaoEsperada, string jsonCanonico, DateTime agoraUtc, CancellationToken ct = default);
}

/// <param name="Json">O que o banco tem (o jsonb reordena as chaves), ou <c>null</c> se a loja nunca configurou.</param>
public sealed record RegraImpressaoSalva(string? Json, int Versao, DateTime? AlteradoEm);

/// <param name="Gravada"><c>false</c> quando outra gravação chegou antes.</param>
/// <param name="Versao">A versão nova, ou a atual quando não gravou.</param>
public sealed record GravacaoRegraImpressao(bool Gravada, int Versao);
