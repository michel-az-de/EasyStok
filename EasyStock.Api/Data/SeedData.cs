using EasyStock.Api.Data.Tenants;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data;

/// <summary>
/// Orquestrador do seed multi-tenant. A definição de cada tenant vive em
/// <c>Tenants/&lt;Nome&gt;Seed.cs</c>; helpers compartilhados em
/// <c>SeedData.Helpers.cs</c>.
///
/// Variável de ambiente:
/// - <c>SEED_DEMO_DATA=false</c> ativa <see cref="ExecutarMinimalAsync"/> (apenas 1 admin).
/// </summary>
public static partial class SeedData
{
    public static async Task ExecutarAsync(IServiceProvider services, ILogger logger)
    {
        var seedDemoData = Environment.GetEnvironmentVariable("SEED_DEMO_DATA");
        if (string.Equals(seedDemoData, "false", StringComparison.OrdinalIgnoreCase))
        {
            await ExecutarMinimalAsync(services, logger);
            return;
        }

        var context = services.GetRequiredService<EasyStockDbContext>();
        var agora = ArredondarAoMinuto(DateTime.UtcNow);

        // O tenant é idempotente (upsert). Rodar sem tx explícita elimina o conflito
        // com NpgsqlRetryingExecutionStrategy ("does not support user-initiated transactions").
        // SaveChanges do tenant já é transacional internamente.
        logger.LogInformation("Seed demo — CasaDaBaba");
        await CasaDaBabaSeed.ExecutarAsync(context, agora, logger);
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();

        logger.LogInformation("Seed demo concluído.");
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
        var agora = ArredondarAoMinuto(DateTime.UtcNow);

        // Guard por EMPRESA (não por usuário): o SuperAdminSeed cria um super-admin global
        // (EmpresaId=null) ANTES deste seed; se checássemos Usuarios.Any() o seed de empresa
        // (empresa+loja+admin) nunca rodaria e não haveria login operacional. O corpo é
        // idempotente (upserts), então re-rodar é seguro.
        if (await context.Empresas.AnyAsync())
        {
            logger.LogInformation("Seed minimal: empresa já existe, pulando.");
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

        logger.LogInformation(
            "Seed minimal concluído. Empresa='{Empresa}' Loja='{Loja}' Admin='{Email}'",
            empresaNome, lojaNome, adminEmail);
    }

    /// <summary>
    /// Arredonda ao minuto para evitar drift de auditoria entre runs (importante
    /// para chaves de idempotência em <c>AuditLog</c>, <c>ProdutoAlteracao</c>,
    /// <c>ClienteAlteracao</c>).
    /// </summary>
    internal static DateTime ArredondarAoMinuto(DateTime dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);
}
