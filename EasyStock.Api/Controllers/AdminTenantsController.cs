using EasyStock.Application.UseCases.Admin.CriarTenantPorAdmin;
using EasyStock.Application.UseCases.Admin.VincularWhatsAppTenant;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Infra.Postgre.Data;
using System.Security.Claims;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/tenants")]
[Authorize(Policy = "SuperAdmin")]
// Poda P01 (#1169): sobrou só a operação de plataforma sem substituto — criar empresa,
// consultar, ligar módulos (ADR-0048) e vincular o número da Meta. O painel Admin saiu.
public class AdminTenantsController(
    EasyStockDbContext db,
    IAdminTenantsQueries tenantsQueries,
    ICurrentUserAccessor currentUser,
    AdminAuditService audit,
    CriarTenantPorAdminUseCase criarTenantUseCase,
    ListarFeaturesDoTenantUseCase listarFeaturesUseCase,
    DefinirFeatureDoTenantUseCase definirFeatureUseCase,
    VincularWhatsAppDoTenantUseCase vincularWhatsAppUseCase,
    ILogger<AdminTenantsController> logger) : EasyStockControllerBase
{
    // ─────────────────── Cadastro manual de tenant pelo back-office ───────────────────

    /// <summary>
    /// Cadastra um cliente (tenant) manualmente pelo operador SuperAdmin. Use case típico:
    /// cliente acionou suporte sem conta, ou admin original saiu da empresa e precisamos
    /// recriar acesso. Cria empresa + usuário admin inicial (Starter + trial 14d) com
    /// senha temporária retornada 1x — o operador exibe pro cliente e/ou envia por email.
    /// Justificativa (≥10 chars) obrigatória → AdminAuditLog.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CriarManual([FromBody] CriarTenantManualRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);

        try
        {
            var resultado = await criarTenantUseCase.ExecuteAsync(new CriarTenantPorAdminCommand(
                NomeEmpresa: req!.NomeEmpresa ?? string.Empty,
                Documento: req.Documento,
                NomeAdmin: req.NomeAdmin ?? string.Empty,
                EmailAdmin: req.EmailAdmin ?? string.Empty,
                EnviarEmail: req.EnviarEmail ?? true));

            await audit.LogAsync(
                "AdminCriouTenantManual",
                $"EmpresaId={resultado.TenantId}, Nome={resultado.NomeEmpresa}, AdminEmail={MascararEmail(resultado.EmailAdmin)}, EmailEnviado={resultado.EmailEnviado}",
                tenantId: resultado.TenantId,
                motivo: motivo,
                entidadeAfetadaId: resultado.TenantId);

            // Senha temporária no payload — UI deve exibir 1x e pedir pro operador anotar.
            // Não logar em loggers — vaza em arquivos. Audit log já omite (só guarda metadados).
            return DataCreated($"/api/admin/tenants/{resultado.TenantId}", new
            {
                tenantId = resultado.TenantId,
                usuarioId = resultado.UsuarioId,
                nomeEmpresa = resultado.NomeEmpresa,
                nomeAdmin = resultado.NomeAdmin,
                emailAdmin = resultado.EmailAdmin,
                senhaTemporaria = resultado.SenhaTemporaria,
                emailEnviado = resultado.EmailEnviado,
                emailErro = resultado.EmailErro,
                trialFim = resultado.TrialFim
            });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao cadastrar tenant manualmente");
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao cadastrar cliente.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTenants(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        StatusAssinatura? filtroStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<StatusAssinatura>(status, out var se))
            filtroStatus = se;

        var (items, total) = await tenantsQueries.ListarAsync(page, pageSize, search, filtroStatus);
        return DataPaged(items, total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id)
    {
        var detalhe = await tenantsQueries.ObterDetalheAsync(id);
        if (detalhe is null) return DataNotFound("Tenant não encontrado.");
        // ADM-05 (#742): o detalhe expoe e-mails de usuarios em claro (decisao de produto:
        // manter visivel). Audita a exibicao de PII pra rastreabilidade LGPD — coerente com a
        // busca global mascarada. LogAsync lanca: se nao da pra auditar a exposicao, nao expoe.
        await audit.LogAsync("TenantPiiVisualizado", "Detalhe carregado (inclui e-mails de usuarios em claro).", id);
        return DataOk(detalhe);
    }

    /// <summary>
    /// Features do tenant para a aba "Features" do back-office: o catálogo inteiro com o
    /// estado de cada uma, não só o que já foi gravado — a tela só oferece toggle para o que
    /// vem nesta lista, então um tenant sem linha nenhuma precisa ver o catálogo para poder
    /// ligar o primeiro módulo.
    /// </summary>
    [HttpGet("{id:guid}/features")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarFeatures(Guid id, CancellationToken ct)
    {
        var features = await listarFeaturesUseCase.ExecuteAsync(new ListarFeaturesDoTenantQuery(id), ct);
        return DataOk(features);
    }

    /// <summary>
    /// Liga ou desliga uma feature do tenant. Auditado: mexer no que um cliente enxerga não
    /// pode ser anônimo.
    /// </summary>
    [HttpPatch("{id:guid}/features/{feature}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PatchFeature(
        Guid id, string feature, [FromBody] PatchTenantFeatureRequest req, CancellationToken ct)
    {
        var existe = await db.Empresas.AnyAsync(e => e.Id == id, ct);
        if (!existe) return DataNotFound("Tenant não encontrado.");

        // O JWT emite o claim curto "email"; ClaimTypes.Email só resolve quando o handler
        // mapeia para a URI longa, o que aqui não acontece — sem os dois, a auditoria
        // gravaria um GUID no lugar de quem mexeu.
        var adminEmail = User.FindFirstValue("email")
            ?? User.FindFirstValue(ClaimTypes.Email)
            ?? currentUser.UsuarioId.ToString();
        var alterado = await definirFeatureUseCase.ExecuteAsync(
            new DefinirFeatureDoTenantCommand(id, feature, req.Ativo, adminEmail), ct);

        await audit.LogAsync(
            "TenantFeatureAlterada",
            $"Feature={alterado.Feature}, Ativo={alterado.Ativo}",
            id);

        return DataOk(alterado);
    }

    /// <summary>
    /// Vincula o <c>phone_number_id</c> da Cloud API da Meta à empresa (#1102), ou desvincula com
    /// <c>null</c>. O número roteia o webhook para o tenant e é o remetente das respostas, então
    /// número já usado por outra empresa é 409. Auditado como o toggle de feature.
    /// </summary>
    [HttpPut("{id:guid}/whatsapp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PutWhatsApp(
        Guid id, [FromBody] PutTenantWhatsAppRequest req, CancellationToken ct)
    {
        VinculoWhatsAppResultado resultado;
        try
        {
            resultado = await vincularWhatsAppUseCase.ExecuteAsync(
                new VincularWhatsAppDoTenantCommand(id, req?.PhoneNumberId), ct);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }

        switch (resultado.Status)
        {
            case StatusVinculoWhatsApp.EmpresaNaoEncontrada:
                return DataNotFound("Tenant não encontrado.");
            case StatusVinculoWhatsApp.NumeroEmUsoPorOutraEmpresa:
                return DataConflict("Este phone_number_id já está vinculado a outra empresa.");
            case StatusVinculoWhatsApp.NumeroReservadoDaPlataforma:
                return DataConflict("Este phone_number_id é o número de plataforma do EasyStok e não pode ser vinculado a uma empresa.");
        }

        await audit.LogAsync(
            resultado.Status == StatusVinculoWhatsApp.Desvinculado ? "TenantWhatsAppDesvinculado" : "TenantWhatsAppVinculado",
            $"PhoneNumberId={resultado.PhoneNumberId ?? "(nenhum)"}",
            id);

        return DataOk(new { phoneNumberId = resultado.PhoneNumberId });
    }

    private static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";
        return email[0] + "***@" + email[(at + 1)..];
    }
}

public record PatchTenantFeatureRequest(bool Ativo);
public record PutTenantWhatsAppRequest(string? PhoneNumberId);
public record CriarTenantManualRequest(
    string Motivo,
    string? NomeEmpresa,
    string? Documento,
    string? NomeAdmin,
    string? EmailAdmin,
    bool? EnviarEmail);
