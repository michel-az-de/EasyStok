using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/notificacoes")]
[Authorize(Policy = "SuperAdmin")]
public sealed class AdminNotificacoesController(
    ITemplateRepository templateRepo,
    IRotinaRepository rotinaRepo,
    IConfiguracaoCanalRepository canalRepo,
    IBloqueioNotificacaoRepository bloqueioRepo,
    IConsentimentoRepository consentimentoRepo,
    IVariavelTemplateCatalogoRepository variaveisRepo,
    ICurrentUserAccessor currentUser,
    CriarTemplateUseCase criarTemplate,
    AtualizarTemplateUseCase atualizarTemplate,
    AprovarTemplateUseCase aprovarTemplate,
    PreviewTemplateUseCase previewTemplate,
    PreviewTemplateRawUseCase previewTemplateRaw,
    CriarRotinaUseCase criarRotina,
    AtualizarRotinaUseCase atualizarRotina,
    AtivarRotinaUseCase ativarRotina,
    DesativarRotinaUseCase desativarRotina,
    AtivarKillSwitchUseCase ativarKillSwitch,
    RemoverKillSwitchUseCase removerKillSwitch,
    ListarLogsEnvioUseCase listarLogs,
    EnviarNotificacaoManualUseCase broadcast) : EasyStockControllerBase
{
    private string Admin => currentUser.UsuarioId.ToString();

    // ── Templates ──────────────────────────────────────────────────────────────

    [HttpGet("templates")]
    public async Task<IActionResult> ListarTemplates(
        [FromQuery] Guid? empresaId,
        [FromQuery] string? tipoEvento,
        [FromQuery] string? canal,
        [FromQuery] bool? ativo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        TipoEventoNotificacao? tipo = tipoEvento is not null
            && Enum.TryParse<TipoEventoNotificacao>(tipoEvento, out var t) ? t : null;
        CanalNotificacao? canalEnum = canal is not null
            && Enum.TryParse<CanalNotificacao>(canal, out var c) ? c : null;

        var (items, total) = await templateRepo.ListarAsync(empresaId, tipo, canalEnum, ativo, page, pageSize);
        return DataPaged(items, total, page, pageSize);
    }

    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id)
    {
        var t = await templateRepo.GetByIdAsync(id);
        return t is null ? DataNotFound("Template não encontrado.") : DataOk(t);
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CriarTemplate([FromBody] CriarTemplateRequest req)
    {
        var result = await criarTemplate.ExecuteAsync(new CriarTemplateCommand(
            req.Codigo, req.Nome, req.Canal, req.TipoEvento,
            req.AssuntoTemplate, req.CorpoTemplate,
            req.Idioma ?? "pt-BR", Admin, req.EmpresaId));
        return DataOk(result);
    }

    [HttpPut("templates/{id:guid}")]
    public async Task<IActionResult> AtualizarTemplate(Guid id, [FromBody] AtualizarTemplateRequest req)
    {
        var result = await atualizarTemplate.ExecuteAsync(
            new AtualizarTemplateCommand(id, req.NovoAssunto, req.NovoCorpo, Admin));
        return DataOk(result);
    }

    [HttpPost("templates/{id:guid}/aprovar")]
    public async Task<IActionResult> AprovarTemplate(Guid id)
    {
        var result = await aprovarTemplate.ExecuteAsync(new AprovarTemplateCommand(id, Admin));
        return DataOk(result);
    }

    [HttpPost("templates/preview")]
    public async Task<IActionResult> PreviewTemplate([FromBody] PreviewTemplateRequest req)
    {
        var result = await previewTemplate.ExecuteAsync(
            new PreviewTemplateCommand(req.TemplateId, req.Variaveis ?? new Dictionary<string, object?>()));
        return DataOk(result);
    }

    [HttpPost("templates/preview-raw")]
    public async Task<IActionResult> PreviewTemplateRaw([FromBody] PreviewTemplateRawRequest req)
    {
        var result = await previewTemplateRaw.ExecuteAsync(
            new PreviewTemplateRawCommand(
                req.AssuntoTemplate ?? "",
                req.CorpoTemplate ?? "",
                req.Variaveis ?? new Dictionary<string, object?>()));
        return DataOk(result);
    }

    [HttpGet("variaveis-catalogo")]
    public async Task<IActionResult> ListarVariaveis([FromQuery] string tipoEvento)
    {
        if (!Enum.TryParse<TipoEventoNotificacao>(tipoEvento, out var tipo))
            return DataBadRequest("tipoEvento inválido.");
        var items = await variaveisRepo.ListarPorTipoEventoAsync(tipo);
        return DataOk(items);
    }

    // ── Rotinas ────────────────────────────────────────────────────────────────

    [HttpGet("rotinas")]
    public async Task<IActionResult> ListarRotinas(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? ativa,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);
        var (items, total) = await rotinaRepo.ListarAsync(empresaId, ativa, page, pageSize);
        return DataPaged(items, total, page, pageSize);
    }

    [HttpGet("rotinas/{id:guid}")]
    public async Task<IActionResult> GetRotina(Guid id)
    {
        var r = await rotinaRepo.GetByIdAsync(id);
        return r is null ? DataNotFound("Rotina não encontrada.") : DataOk(r);
    }

    [HttpPost("rotinas")]
    public async Task<IActionResult> CriarRotina([FromBody] CriarRotinaRequest req)
    {
        var result = await criarRotina.ExecuteAsync(new CriarRotinaCommand(
            req.Codigo, req.Nome, req.TipoEvento, req.TriggerTipo,
            req.TemplateCodigo, req.Categoria,
            req.CronExpression, req.ParametrosJson, req.EmpresaId));
        return DataOk(result);
    }

    [HttpPatch("rotinas/{id:guid}")]
    public async Task<IActionResult> AtualizarRotina(Guid id, [FromBody] AtualizarRotinaRequest req)
    {
        var result = await atualizarRotina.ExecuteAsync(
            new AtualizarRotinaCommand(id, req.CronExpression, req.ParametrosJson, Admin));
        return DataOk(result);
    }

    [HttpPatch("rotinas/{id:guid}/ativar")]
    public async Task<IActionResult> AtivarRotina(Guid id)
    {
        var result = await ativarRotina.ExecuteAsync(new AtivarRotinaCommand(id, Admin));
        return DataOk(result);
    }

    [HttpPatch("rotinas/{id:guid}/desativar")]
    public async Task<IActionResult> DesativarRotina(Guid id)
    {
        var result = await desativarRotina.ExecuteAsync(new DesativarRotinaCommand(id, Admin));
        return DataOk(result);
    }

    // ── Canais / Kill-switch ────────────────────────────────────────────────────

    [HttpGet("canais")]
    public async Task<IActionResult> ListarCanais([FromQuery] Guid? empresaId = null)
    {
        var configs = await canalRepo.ListarAsync(empresaId);
        var bloqueios = await bloqueioRepo.ListarAtivosAsync(null, null);
        return DataOk(new { configs, bloqueios });
    }

    [HttpPost("canais/kill-switch")]
    public async Task<IActionResult> AtivarKillSwitch([FromBody] KillSwitchRequest req)
    {
        CanalNotificacao? canal = req.Canal is not null
            && Enum.TryParse<CanalNotificacao>(req.Canal, out var c) ? c : null;

        var result = await ativarKillSwitch.ExecuteAsync(new AtivarKillSwitchCommand(
            req.Motivo, Admin, req.EmpresaId, canal, req.ExpiraEm));
        return DataOk(result);
    }

    [HttpDelete("canais/kill-switch/{id:guid}")]
    public async Task<IActionResult> RemoverKillSwitch(Guid id)
    {
        var result = await removerKillSwitch.ExecuteAsync(new RemoverKillSwitchCommand(id, Admin));
        return DataOk(result);
    }

    // ── Logs de envio ──────────────────────────────────────────────────────────

    [HttpGet("envios")]
    public async Task<IActionResult> ListarEnvios(
        [FromQuery] Guid? empresaId,
        [FromQuery] string? status,
        [FromQuery] string? canal,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        StatusOutbox? statusEnum = status is not null
            && Enum.TryParse<StatusOutbox>(status, out var s) ? s : null;
        CanalNotificacao? canalEnum = canal is not null
            && Enum.TryParse<CanalNotificacao>(canal, out var c) ? c : null;

        var result = await listarLogs.ExecuteAsync(new ListarLogsEnvioQuery(
            empresaId, statusEnum, canalEnum, de, ate, page, pageSize));

        return DataPaged(result.Items, result.TotalCount, result.Page, result.PageSize);
    }

    // ── Consentimentos ─────────────────────────────────────────────────────────

    [HttpGet("consentimentos")]
    public async Task<IActionResult> ListarConsentimentos([FromQuery] Guid usuarioId)
    {
        var items = await consentimentoRepo.ListarPorUsuarioAsync(usuarioId);
        return DataOk(items);
    }

    // ── Broadcast ─────────────────────────────────────────────────────────────

    [HttpPost("broadcast")]
    public async Task<IActionResult> EnviarBroadcast([FromBody] BroadcastRequest req)
    {
        var result = await broadcast.ExecuteAsync(new EnviarNotificacaoManualCommand(
            req.EmpresaId, req.UsuariosDestinoIds,
            req.Titulo, req.Mensagem, Admin));
        return DataOk(new { enviado = true, totalEnfileirado = result.TotalEnfileirado });
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────

    public record CriarTemplateRequest(
        string Codigo, string Nome,
        CanalNotificacao Canal, TipoEventoNotificacao TipoEvento,
        string AssuntoTemplate, string CorpoTemplate,
        string? Idioma = null, Guid? EmpresaId = null);

    public record AtualizarTemplateRequest(string NovoAssunto, string NovoCorpo);

    public record PreviewTemplateRequest(
        Guid TemplateId,
        IDictionary<string, object?>? Variaveis = null);

    public record PreviewTemplateRawRequest(
        string? AssuntoTemplate,
        string? CorpoTemplate,
        IDictionary<string, object?>? Variaveis = null);

    public record CriarRotinaRequest(
        string Codigo, string Nome,
        TipoEventoNotificacao TipoEvento, TriggerTipoRotina TriggerTipo,
        string TemplateCodigo, CategoriaConteudoNotificacao Categoria,
        string? CronExpression = null, string? ParametrosJson = null,
        Guid? EmpresaId = null);

    public record AtualizarRotinaRequest(
        string? CronExpression = null,
        string? ParametrosJson = null);

    public record KillSwitchRequest(
        string Motivo,
        Guid? EmpresaId = null,
        string? Canal = null,
        DateTime? ExpiraEm = null);

    public record BroadcastRequest(
        Guid EmpresaId,
        List<Guid> UsuariosDestinoIds,
        string Titulo,
        string Mensagem);
}
