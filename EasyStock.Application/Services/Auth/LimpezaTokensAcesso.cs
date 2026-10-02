namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Limpeza dos segredos de acesso (N8): apaga em lote os <c>reset_tokens</c> que expiraram há mais de 24 h, usados ou
/// não. A repetição a cada 6 h é do <c>LimpezaTokensAcessoService</c> (Worker); aqui fica a regra, testável sem relógio real.
/// </summary>
public sealed class LimpezaTokensAcesso(
    IResetTokenRepository tokens, TimeProvider relogio, ILogger<LimpezaTokensAcesso> logger)
{
    public static readonly TimeSpan Carencia = TimeSpan.FromHours(24);

    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var corte = relogio.GetUtcNow().UtcDateTime - Carencia;
        var apagados = await tokens.ApagarExpiradosAsync(corte, ct);
        if (apagados > 0)
            logger.LogInformation(
                "Limpeza de segredos de acesso: {Apagados} reset_tokens expirados antes de {Corte:O} apagados", apagados, corte);
        return apagados;
    }
}
