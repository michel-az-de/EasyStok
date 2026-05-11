using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Data.Interceptors;

/// <summary>
/// Emite <c>SET app.empresa_id</c> e <c>SET app.bypass_rls</c> em cada
/// abertura de conexão Npgsql, e <c>RESET</c> no fechamento. Essas variáveis
/// alimentam a policy <c>tenant_isolation</c> definida na migration
/// <c>20260511120000_AddRowLevelSecurity</c>.
///
/// <para>
/// <b>Por que SET (não SET LOCAL):</b> npgsql usa connection pooling — uma
/// mesma conexão física serve várias requests. <c>SET LOCAL</c> só vive
/// durante uma transação; queries fora de transação (a maioria das leituras)
/// ficariam sem tenant. <c>SET</c> sem LOCAL persiste pelo tempo da sessão,
/// mas RESET no <c>ConnectionClosing</c> garante que a próxima request que
/// reciclar essa conexão começa limpa.
/// </para>
///
/// <para>
/// <b>Defesa contra reentrada de pool:</b> se o RESET falhar (rede caiu,
/// pool fez drop forçado) e a próxima request reusa a conexão, o
/// <c>ConnectionOpenedAsync</c> sempre re-emite o SET — então o tenant da
/// request anterior é sobrescrito antes que qualquer query rode.
/// </para>
///
/// <para>
/// <b>SuperAdmin/jobs/seeds:</b> não usam role com BYPASSRLS no banco; em
/// vez disso, setam <see cref="EasyStockDbContext.BypassRowLevelSecurity"/>
/// via <see cref="EasyStockDbContext.UseRowLevelSecurityBypass"/>. O
/// interceptor lê esse flag aqui e emite <c>SET app.bypass_rls = 'true'</c>.
/// Vantagem: auditável via código (não depende de privilégio de role) e
/// fácil de escopar com <c>using</c> blocks.
/// </para>
/// </summary>
public sealed class SetTenantOnConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<SetTenantOnConnectionInterceptor>? _logger;

    public SetTenantOnConnectionInterceptor(ILogger<SetTenantOnConnectionInterceptor>? logger = null)
    {
        _logger = logger;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantSettingsAsync(connection, eventData.Context, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyTenantSettingsSync(connection, eventData.Context);
        base.ConnectionOpened(connection, eventData);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await ResetTenantSettingsAsync(connection);
        return await base.ConnectionClosingAsync(connection, eventData, result);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ResetTenantSettingsSync(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    private async Task ApplyTenantSettingsAsync(
        DbConnection connection,
        Microsoft.EntityFrameworkCore.DbContext? context,
        CancellationToken ct)
    {
        // Conexões abertas fora do EF (ex.: health check via Database.OpenConnectionAsync
        // sem usar Set<T>) entram aqui com Context=null. Aplicamos RESET pra deixar
        // a sessão limpa — se o caller precisar de tenant ele tem que ir via DbContext.
        if (context is not EasyStockDbContext db)
        {
            await ResetTenantSettingsAsync(connection);
            return;
        }

        var bypass = db.BypassRowLevelSecurity;
        var tenantId = db.CurrentTenantId;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildSetCommand(tenantId, bypass);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[RLS] Falha ao aplicar tenant na conexão (bypass={Bypass}, tenant={Tenant}). Conexão NÃO será usada pra evitar vazamento.",
                bypass, tenantId);
            throw;
        }
    }

    private void ApplyTenantSettingsSync(
        DbConnection connection,
        Microsoft.EntityFrameworkCore.DbContext? context)
    {
        if (context is not EasyStockDbContext db)
        {
            ResetTenantSettingsSync(connection);
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildSetCommand(db.CurrentTenantId, db.BypassRowLevelSecurity);
        try { cmd.ExecuteNonQuery(); }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[RLS] Falha ao aplicar tenant na conexão (sync). Conexão NÃO será usada.");
            throw;
        }
    }

    private async Task ResetTenantSettingsAsync(DbConnection connection)
    {
        // ConnectionClosing pode ser chamado em estados onde a conexão já
        // está corrompida/desconectada. RESET é best-effort: o que importa
        // é que a próxima ApplyTenantSettings re-emita o SET antes de qualquer
        // query, garantindo que nenhum tenant residual escape.
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "RESET app.empresa_id; RESET app.bypass_rls;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[RLS] RESET no fechamento falhou (best-effort).");
        }
    }

    private void ResetTenantSettingsSync(DbConnection connection)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "RESET app.empresa_id; RESET app.bypass_rls;";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[RLS] RESET no fechamento (sync) falhou (best-effort).");
        }
    }

    private static string BuildSetCommand(Guid tenantId, bool bypass)
    {
        // SET (sem LOCAL): persiste pelo tempo da sessão Npgsql. ConnectionClosing
        // emite RESET pra evitar que a próxima request reusando a conexão veja
        // o tenant anterior. Mesmo se RESET falhar, ApplyTenantSettings na
        // próxima abertura sobrescreve antes de qualquer query rodar.
        //
        // Quando tenantId == Guid.Empty (request sem JWT, login, jobs sem bypass)
        // emitimos string vazia — a policy converte com NULLIF para NULL e a
        // comparação resulta em 0 linhas. Combinado com IsSuperAdmin=false no
        // DbContext, isso é a postura fail-closed esperada.
        var tenantLiteral = tenantId == Guid.Empty
            ? string.Empty
            : tenantId.ToString();

        // EscapeSqlString aqui é zelo: tenantId é Guid (formato fixo), bypass é
        // bool. Sem possibilidade de injection, mas mantém o pattern caso alguém
        // amplie o método no futuro.
        return $"SET app.empresa_id = '{tenantLiteral}'; SET app.bypass_rls = '{(bypass ? "true" : "false")}';";
    }
}
