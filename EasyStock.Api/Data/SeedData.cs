using EasyStock.Api.Data.Tenants;
using EasyStock.Api.Data.Tenants.MassasVeneza;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data;

/// <summary>
/// Orquestrador do seed multi-tenant. A definição de cada tenant vive em
/// <c>Tenants/&lt;Nome&gt;Seed.cs</c>; helpers compartilhados em
/// <c>SeedData.Helpers.cs</c> e <c>SeedData.NewHelpers.cs</c>.
///
/// Variáveis de ambiente:
/// - <c>SEED_DEMO_DATA=false</c> ativa <see cref="ExecutarMinimalAsync"/> (apenas 1 admin).
/// - <c>SEED_DEMO_VOLUME=small|medium|large</c> seleciona quais tenants rodam.
///   Default: <c>large</c> (4 tenants).
/// </summary>
public static partial class SeedData
{
    private const long AdvisoryLockKey = 840240017320251L;

    public enum Volume { Small, Medium, Large }

    public static async Task ExecutarAsync(IServiceProvider services, ILogger logger)
    {
        var seedDemoData = Environment.GetEnvironmentVariable("SEED_DEMO_DATA");
        if (string.Equals(seedDemoData, "false", StringComparison.OrdinalIgnoreCase))
        {
            await ExecutarMinimalAsync(services, logger);
            return;
        }

        var volume = ParseVolume(Environment.GetEnvironmentVariable("SEED_DEMO_VOLUME"));
        logger.LogInformation("Seed demo iniciando — volume={Volume}", volume);

        var context = services.GetRequiredService<EasyStockDbContext>();
        // Schema bootstrap defensivo — mesmo de SeedData via startup hook ou
        // chamada direta. Garante IsSeedData/SeedRunLogs antes de tocar Empresas.
        await SeedSchemaBootstrap.EnsureAsync(context, logger);
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            var agora = ArredondarAoMinuto(DateTime.UtcNow);

            await using var tx = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})");

            // T1 — sempre roda (custo trivial; força fluxo de onboarding)
            await PastaBellaSpSeed.ExecutarAsync(context, agora, logger);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // T2 — sempre roda (small já cobre)
            await CantinaMauricioSeed.ExecutarAsync(context, agora, logger);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            if (volume >= Volume.Medium)
            {
                await CasaDaBabaSeed.ExecutarAsync(context, agora, logger);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            if (volume >= Volume.Large)
            {
                await MassasVenezaSeed.ExecutarAsync(context, agora, logger);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            await tx.CommitAsync();
            logger.LogInformation("Seed demo concluído.");
        });
    }

    public static async Task ExecutarMinimalAsync(IServiceProvider services, ILogger logger)
    {
        var empresaNome = Environment.GetEnvironmentVariable("SEED_EMPRESA_NOME") ?? "Minha Empresa";
        var empresaDoc  = Environment.GetEnvironmentVariable("SEED_EMPRESA_DOCUMENTO") ?? "00.000.000/0001-00";
        var lojaNome    = Environment.GetEnvironmentVariable("SEED_LOJA_NOME") ?? "Loja Principal";
        var adminNome   = Environment.GetEnvironmentVariable("SEED_ADMIN_NOME") ?? "Administrador";
        var adminEmail  = Environment.GetEnvironmentVariable("SEED_ADMIN_EMAIL") ?? "admin@easystock.local";
        var adminSenha  = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD") ?? "Admin@123";

        var context  = services.GetRequiredService<EasyStockDbContext>();
        // Schema bootstrap defensivo (mesma justificativa do ExecutarAsync acima).
        await SeedSchemaBootstrap.EnsureAsync(context, logger);
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            var agora = ArredondarAoMinuto(DateTime.UtcNow);

            await using var tx = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})");

            if (await context.Usuarios.AnyAsync())
            {
                logger.LogInformation("Seed minimal: usuário já existe, pulando.");
                await tx.CommitAsync();
                return;
            }

            logger.LogInformation("Executando seed minimal para '{Empresa}'...", empresaNome);

            var plano = await context.Planos.FirstOrDefaultAsync();
            if (plano is null)
            {
                plano = new Plano
                {
                    Id = Guid.NewGuid(),
                    Nome = "Plano Local",
                    Descricao = "Plano para uso local",
                    LimiteLojas = Plano.SemLimite,
                    LimiteUsuarios = Plano.SemLimite,
                    LimiteProdutos = Plano.SemLimite,
                    LimiteGeracoesIaMensais = Plano.SemLimite,
                    PrecoMensal = 0m,
                    Ativo = true,
                    CriadoEm = agora
                };
                context.Planos.Add(plano);
            }

            var empresa = await context.Empresas.FirstOrDefaultAsync(e => e.Documento == empresaDoc)
                       ?? await context.Empresas.FirstOrDefaultAsync(e => e.Nome == empresaNome);
            if (empresa is null)
            {
                empresa = Empresa.Criar(empresaNome, empresaDoc);
                context.Empresas.Add(empresa);
            }

            var assinatura = await context.AssinaturasEmpresa.FirstOrDefaultAsync(a => a.EmpresaId == empresa.Id);
            if (assinatura is null)
            {
                context.AssinaturasEmpresa.Add(new AssinaturaEmpresa
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresa.Id,
                    PlanoId = plano.Id,
                    DataInicio = agora.Date,
                    DataFim = agora.Date.AddYears(10),
                    Status = StatusAssinatura.Ativa,
                    CriadoEm = agora
                });
            }

            var loja = await context.Lojas.FirstOrDefaultAsync(l => l.EmpresaId == empresa.Id);
            if (loja is null)
            {
                loja = Loja.Criar(empresa.Id, lojaNome);
                loja.Ativa = true;
                context.Lojas.Add(loja);
            }

            if (!await context.ConfiguracoesLoja.AnyAsync(c => c.LojaId == loja.Id))
                context.ConfiguracoesLoja.Add(ConfiguracaoLoja.CriarPadrao(loja.Id));

            var perfilAdmin = await UpsertPerfilAsync(context, empresa.Id, "Admin", "Administrador com acesso total", NivelAcesso.Admin, agora);
            await UpsertPerfilAsync(context, empresa.Id, "Gerente", "Gestão operacional e analytics", NivelAcesso.Gerente, agora);
            await UpsertPerfilAsync(context, empresa.Id, "Operador", "Operação diária de estoque e vendas", NivelAcesso.Operador, agora);

            var usuario = await UpsertUsuarioAsync(context, adminNome, adminEmail, adminSenha, agora);

            await EnsureUsuarioEmpresaAsync(context, usuario.Id, empresa.Id, agora);
            await EnsureUsuarioPerfilAsync(context, usuario.Id, empresa.Id, perfilAdmin.Id, null, agora);

            await context.SaveChangesAsync();
            await tx.CommitAsync();

            logger.LogInformation(
                "Seed minimal concluído. Empresa='{Empresa}' Loja='{Loja}' Admin='{Email}'",
                empresaNome, lojaNome, adminEmail);
        });
    }

    private static Volume ParseVolume(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "small" => Volume.Small,
        "medium" => Volume.Medium,
        "large" or "" => Volume.Large,
        _ => Volume.Large
    };

    /// <summary>
    /// Arredonda ao minuto para evitar drift de auditoria entre runs (importante
    /// para chaves de idempotência em <c>AuditLog</c>, <c>ProdutoAlteracao</c>,
    /// <c>ClienteAlteracao</c>).
    /// </summary>
    internal static DateTime ArredondarAoMinuto(DateTime dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);
}
