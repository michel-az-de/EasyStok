using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.AspNetCore.Mvc.Filters;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Templates, rotinas e canais de notificação no contexto da empresa (P01-B, #1173/#1176): o que o
/// painel Admin fazia em <c>api/admin/notificacoes</c>. Divide o prefixo <c>api/notificacoes</c> com o
/// <see cref="NotificacaoController"/>, que é a caixa de alertas in-app de qualquer usuário; aqui é
/// configuração, só para o admin da empresa. A empresa sai sempre do token. Recurso de outra empresa
/// ou global responde 404, e template global não é editável pelo tenant.
/// </summary>
[SwaggerTag("Notification settings (tenant)")]
[ApiController]
[Route("api/notificacoes")]
[Authorize(Policy = "Admin")]
public sealed class NotificacoesConfiguracaoController(
    ITemplateRepository templateRepo,
    IRotinaRepository rotinaRepo,
    IConfiguracaoCanalRepository canalRepo,
    IBloqueioNotificacaoRepository bloqueioRepo,
    IVariavelTemplateCatalogoRepository variaveisRepo,
    ICurrentUserAccessor currentUser,
    CriarTemplateUseCase criarTemplate,
    AtualizarTemplateUseCase atualizarTemplate,
    AprovarTemplateUseCase aprovarTemplate,
    PreviewTemplateUseCase previewTemplate,
    PreviewDraftTemplateUseCase previewDraftTemplate,
    CriarRotinaUseCase criarRotina,
    AtualizarRotinaUseCase atualizarRotina,
    AtivarRotinaUseCase ativarRotina,
    DesativarRotinaUseCase desativarRotina,
    AtivarKillSwitchUseCase ativarKillSwitch,
    RemoverKillSwitchUseCase removerKillSwitch,
    ListarLogsEnvioUseCase listarLogs) : EasyStockControllerBase, IActionFilter
{
    void IActionFilter.OnActionExecuting(ActionExecutingContext context)
    {
        if (currentUser.EmpresaId == Guid.Empty)
            context.Result = DataBadRequest(
                "Sua sessão não está vinculada a uma empresa. Selecione uma empresa para configurar as notificações.");
    }

    void IActionFilter.OnActionExecuted(ActionExecutedContext context) { }

    private Guid Empresa => currentUser.EmpresaId;
    private string Usuario => currentUser.UsuarioId.ToString();

    private async Task<IActionResult> NoEscopo(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (ConfiguracaoNotificacaoNaoEncontradaException ex) { return DataNotFound(ex.Message); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }

    private static T? Enumerado<T>(string? valor) where T : struct, Enum =>
        valor is not null && Enum.TryParse<T>(valor, out var v) ? v : null;

    // ── Templates ──────────────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List the company's notification templates")]
    [HttpGet("templates")]
    public async Task<IActionResult> ListarTemplates(
        [FromQuery] string? tipoEvento,
        [FromQuery] string? canal,
        [FromQuery] bool? ativo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);
        var (items, total) = await templateRepo.ListarAsync(
            Empresa, Enumerado<TipoEventoNotificacao>(tipoEvento), Enumerado<CanalNotificacao>(canal),
            ativo, page, pageSize);
        return DataPaged(items, total, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get a company notification template")]
    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id)
    {
        var t = await templateRepo.GetByIdAsync(id);
        return t is null || t.EmpresaId != Empresa ? DataNotFound("Template não encontrado.") : DataOk(t);
    }

    [SwaggerOperation(Summary = "Create a company notification template")]
    [HttpPost("templates")]
    public Task<IActionResult> CriarTemplate([FromBody] CriarTemplateRequest req) => NoEscopo(async () =>
        DataOk(await criarTemplate.ExecuteAsync(new CriarTemplateCommand(
            req.Codigo, req.Nome, req.Canal, req.TipoEvento,
            req.AssuntoTemplate, req.CorpoTemplate,
            req.Idioma ?? "pt-BR", Usuario, Empresa))));

    [SwaggerOperation(Summary = "Update a company template (creates a new version)")]
    [HttpPut("templates/{id:guid}")]
    public Task<IActionResult> AtualizarTemplate(Guid id, [FromBody] AtualizarTemplateRequest req) => NoEscopo(async () =>
        DataOk(await atualizarTemplate.ExecuteAsync(
            new AtualizarTemplateCommand(id, req.NovoAssunto, req.NovoCorpo, Usuario, Empresa))));

    [SwaggerOperation(Summary = "Approve a company template")]
    [HttpPost("templates/{id:guid}/aprovar")]
    public Task<IActionResult> AprovarTemplate(Guid id) => NoEscopo(async () =>
        DataOk(await aprovarTemplate.ExecuteAsync(new AprovarTemplateCommand(id, Usuario, Empresa))));

    [SwaggerOperation(Summary = "Render a saved company template")]
    [HttpPost("templates/preview")]
    public Task<IActionResult> PreviewTemplate([FromBody] PreviewTemplateRequest req) => NoEscopo(async () =>
        DataOk(await previewTemplate.ExecuteAsync(new PreviewTemplateCommand(
            req.TemplateId, req.Variaveis ?? new Dictionary<string, object?>(), Empresa))));

    [SwaggerOperation(Summary = "Render an unsaved template draft (live preview)")]
    [HttpPost("templates/preview-draft")]
    public async Task<IActionResult> PreviewDraftTemplate([FromBody] PreviewDraftRequest req) =>
        DataOk(await previewDraftTemplate.ExecuteAsync(new PreviewDraftTemplateCommand(
            req.AssuntoTemplate ?? string.Empty,
            req.CorpoTemplate ?? string.Empty,
            req.Variaveis ?? new Dictionary<string, object?>())));

    [SwaggerOperation(Summary = "List the variables available for an event type")]
    [HttpGet("variaveis-catalogo")]
    public async Task<IActionResult> ListarVariaveis([FromQuery] string tipoEvento)
    {
        if (!Enum.TryParse<TipoEventoNotificacao>(tipoEvento, out var tipo))
            return DataBadRequest("tipoEvento inválido.");
        return DataOk(await variaveisRepo.ListarPorTipoEventoAsync(tipo));
    }

    // ── Rotinas ────────────────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List the company's notification routines")]
    [HttpGet("rotinas")]
    public async Task<IActionResult> ListarRotinas(
        [FromQuery] bool? ativa,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);
        var (items, total) = await rotinaRepo.ListarAsync(Empresa, ativa, page, pageSize);
        return DataPaged(items, total, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get a company notification routine")]
    [HttpGet("rotinas/{id:guid}")]
    public async Task<IActionResult> GetRotina(Guid id)
    {
        var r = await rotinaRepo.GetByIdAsync(id);
        return r is null || r.EmpresaId != Empresa ? DataNotFound("Rotina não encontrada.") : DataOk(r);
    }

    [SwaggerOperation(Summary = "Create a company notification routine")]
    [HttpPost("rotinas")]
    public Task<IActionResult> CriarRotina([FromBody] CriarRotinaRequest req) => NoEscopo(async () =>
        DataOk(await criarRotina.ExecuteAsync(new CriarRotinaCommand(
            req.Codigo, req.Nome, req.TipoEvento, req.TriggerTipo,
            req.TemplateCodigo, req.Categoria,
            req.CronExpression, req.ParametrosJson, Empresa))));

    [SwaggerOperation(Summary = "Update schedule or parameters of a company routine")]
    [HttpPatch("rotinas/{id:guid}")]
    public Task<IActionResult> AtualizarRotina(Guid id, [FromBody] AtualizarRotinaRequest req) => NoEscopo(async () =>
        DataOk(await atualizarRotina.ExecuteAsync(
            new AtualizarRotinaCommand(id, req.CronExpression, req.ParametrosJson, Usuario, Empresa))));

    [SwaggerOperation(Summary = "Activate a company routine")]
    [HttpPatch("rotinas/{id:guid}/ativar")]
    public Task<IActionResult> AtivarRotina(Guid id) => NoEscopo(async () =>
        DataOk(await ativarRotina.ExecuteAsync(new AtivarRotinaCommand(id, Usuario, Empresa))));

    [SwaggerOperation(Summary = "Deactivate a company routine")]
    [HttpPatch("rotinas/{id:guid}/desativar")]
    public Task<IActionResult> DesativarRotina(Guid id) => NoEscopo(async () =>
        DataOk(await desativarRotina.ExecuteAsync(new DesativarRotinaCommand(id, Usuario, Empresa))));

    // ── Canais / kill switch ───────────────────────────────────────────────────

    /// <summary>Configuração de canal da empresa e bloqueios ativos (os globais aparecem, só leitura).</summary>
    [SwaggerOperation(Summary = "List the company's channel settings and active blocks")]
    [HttpGet("canais")]
    public async Task<IActionResult> ListarCanais()
    {
        var configs = await canalRepo.ListarAsync(Empresa);
        var bloqueios = await bloqueioRepo.ListarAtivosAsync(Empresa, null);
        return DataOk(new { configs, bloqueios });
    }

    [SwaggerOperation(Summary = "Pause the company's notifications (all channels or one)")]
    [HttpPost("canais/kill-switch")]
    public async Task<IActionResult> AtivarKillSwitch([FromBody] KillSwitchRequest req) =>
        DataOk(await ativarKillSwitch.ExecuteAsync(new AtivarKillSwitchCommand(
            req.Motivo, Usuario, Empresa, Enumerado<CanalNotificacao>(req.Canal), req.ExpiraEm)));

    [SwaggerOperation(Summary = "Remove a company notification pause")]
    [HttpDelete("canais/kill-switch/{id:guid}")]
    public Task<IActionResult> RemoverKillSwitch(Guid id) => NoEscopo(async () =>
        DataOk(await removerKillSwitch.ExecuteAsync(new RemoverKillSwitchCommand(id, Usuario, Empresa))));

    // ── Logs de envio ──────────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List the company's notification delivery log")]
    [HttpGet("envios")]
    public async Task<IActionResult> ListarEnvios(
        [FromQuery] string? status,
        [FromQuery] string? canal,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);
        var result = await listarLogs.ExecuteAsync(new ListarLogsEnvioQuery(
            Empresa, Enumerado<StatusOutbox>(status), Enumerado<CanalNotificacao>(canal), de, ate, page, pageSize));
        return DataPaged(result.Items, result.TotalCount, result.Page, result.PageSize);
    }

    // ── Request DTOs (sem EmpresaId: o escopo vem do token) ────────────────────

    public sealed record CriarTemplateRequest(
        string Codigo, string Nome,
        CanalNotificacao Canal, TipoEventoNotificacao TipoEvento,
        string AssuntoTemplate, string CorpoTemplate,
        string? Idioma = null);

    public sealed record AtualizarTemplateRequest(string NovoAssunto, string NovoCorpo);

    public sealed record PreviewTemplateRequest(
        Guid TemplateId,
        IDictionary<string, object?>? Variaveis = null);

    public sealed record PreviewDraftRequest(
        string? AssuntoTemplate,
        string? CorpoTemplate,
        IDictionary<string, object?>? Variaveis = null);

    public sealed record CriarRotinaRequest(
        string Codigo, string Nome,
        TipoEventoNotificacao TipoEvento, TriggerTipoRotina TriggerTipo,
        string TemplateCodigo, CategoriaConteudoNotificacao Categoria,
        string? CronExpression = null, string? ParametrosJson = null);

    public sealed record AtualizarRotinaRequest(
        string? CronExpression = null,
        string? ParametrosJson = null);

    public sealed record KillSwitchRequest(
        string Motivo,
        string? Canal = null,
        DateTime? ExpiraEm = null);
}
