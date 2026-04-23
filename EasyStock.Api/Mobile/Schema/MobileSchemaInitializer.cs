using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Schema;

/// <summary>
/// Aplica o schema do módulo Mobile via SQL raw no startup da API.
///
/// O arquivo <c>001_CreateMobileSchema.sql</c> é idempotente
/// (usa <c>CREATE TABLE IF NOT EXISTS</c> + <c>INSERT ... ON CONFLICT DO NOTHING</c>),
/// então rodar múltiplas vezes é seguro. Não entra no <c>__EFMigrationsHistory</c>;
/// é uma decisão consciente para manter o módulo isolado do pipeline EF.
/// </summary>
public static class MobileSchemaInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default)
    {
        var sqlPath = Path.Combine(AppContext.BaseDirectory, "Mobile", "Schema", "001_CreateMobileSchema.sql");
        if (!File.Exists(sqlPath))
        {
            logger.LogWarning(
                "Mobile schema SQL não encontrado em {Path}; endpoints /api/mobile/* vão falhar até o arquivo ser publicado.",
                sqlPath);
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var sql = await File.ReadAllTextAsync(sqlPath, ct);
        await db.Database.ExecuteSqlRawAsync(sql, ct);

        logger.LogInformation("Mobile schema aplicado ({Path}).", sqlPath);
    }
}
