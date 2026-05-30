namespace EasyStock.Api.Http;

/// <summary>
/// Guardas defensivos puros para entradas HTTP no Api, espelhando
/// <see cref="EasyStock.Application.UseCases.Common.UseCaseGuards"/> no
/// Application: helpers estaticos sem dependencia de HTTP, testaveis isolados.
///
/// Pareados com helpers HTTP-aware em <see cref="EasyStockControllerBase"/>
/// (ex: TryEnsureNotEmpty): aqui mora a logica pura (string -> bool + out);
/// la mora o que precisa de IActionResult.
/// </summary>
public static class RequestGuards
{
    /// <summary>
    /// Valida o motivo (justificativa) de operacoes Admin auditadas.
    /// Defaults preservam o contrato historico dos controllers Admin
    /// (minimo 10 caracteres, maximo 1000) — mensagens copiadas literal
    /// do ValidarMotivo privado pre-A1 (R8).
    /// </summary>
    public static bool TryValidarMotivo(
        string? raw,
        out string normalizado,
        out string? erro,
        int minLen = 10,
        int maxLen = 1000)
    {
        normalizado = (raw ?? string.Empty).Trim();
        if (normalizado.Length < minLen)
        {
            erro = $"Justificativa obrigatória (mínimo {minLen} caracteres) — fica registrada no audit log.";
            return false;
        }
        if (normalizado.Length > maxLen)
        {
            erro = $"Justificativa muito longa (máx {maxLen} caracteres).";
            return false;
        }
        erro = null;
        return true;
    }
}
