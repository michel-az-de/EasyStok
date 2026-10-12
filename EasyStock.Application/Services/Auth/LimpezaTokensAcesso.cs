namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Limpeza dos segredos de acesso (N8): apaga em lote os <c>reset_tokens</c> que expiraram há mais de 24 h, usados ou
/// não, e, com a mesma carência, os <c>refresh_tokens</c> e os <c>email_confirmation_tokens</c> (#1566: as duas tabelas
/// só cresciam). A repetição a cada 6 h é do <c>LimpezaTokensAcessoService</c> (Worker); aqui fica a regra, testável sem
/// relógio real. A falha numa tabela não segura as outras.
/// </summary>
public sealed class LimpezaTokensAcesso(
    IResetTokenRepository tokens, IRefreshTokenRepository refreshTokens, IEmailConfirmationTokenRepository tokensDeEmail,
    TimeProvider relogio, ILogger<LimpezaTokensAcesso> logger)
{
    public static readonly TimeSpan Carencia = TimeSpan.FromHours(24);

    /// <returns>Total de linhas apagadas nas três tabelas.</returns>
    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var corte = relogio.GetUtcNow().UtcDateTime - Carencia;
        return await ApagarAsync("reset_tokens", () => tokens.ApagarExpiradosAsync(corte, ct), corte, ct)
             + await ApagarAsync("refresh_tokens", () => refreshTokens.DeleteExpiredAsync(corte, ct), corte, ct)
             + await ApagarAsync("email_confirmation_tokens", () => tokensDeEmail.DeleteExpiredAsync(corte, ct), corte, ct);
    }

    private async Task<int> ApagarAsync(string tabela, Func<Task<int>> apagar, DateTime corte, CancellationToken ct)
    {
        try
        {
            var apagados = await apagar();
            if (apagados > 0)
                logger.LogInformation(
                    "Limpeza de segredos de acesso: {Apagados} {Tabela} expirados antes de {Corte:O} apagados", apagados, tabela, corte);
            return apagados;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Limpeza de segredos de acesso: falha ao apagar {Tabela} expirados; as outras tabelas seguem", tabela);
            return 0;
        }
    }
}
