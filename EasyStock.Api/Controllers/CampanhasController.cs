using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Campanhas da dona (S28): cadastro, agendamento e cancelamento; público com exclusões e limite
/// semanal (S29). O disparo (S30) fica fora. A arte sobe por <c>POST api/uploads/campanha/arte</c> e a URL devolvida vai em <c>imagemUrl</c>.
/// Policy <c>Gerente</c>: campanha de marketing custa por mensagem na Meta.
/// </summary>
[SwaggerTag("Campaigns")]
[ApiController]
[Route("api/campanhas")]
[Authorize(Policy = "Gerente")]
[ValidateEmpresaId]
public class CampanhasController(
    ListarCampanhasUseCase listarUseCase,
    ObterCampanhaUseCase obterUseCase,
    CriarCampanhaUseCase criarUseCase,
    AtualizarCampanhaUseCase atualizarUseCase,
    CancelarCampanhaUseCase cancelarUseCase,
    CalcularPublicoCampanhaUseCase calcularPublicoUseCase,
    ListarDestinatariosCampanhaUseCase listarDestinatariosUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List campaigns (most recent first, up to 100)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public Task<IActionResult> Listar([FromQuery] StatusCampanha? status, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await listarUseCase.ExecuteAsync(emp, status, ct)));

    [SwaggerOperation(Summary = "Get campaign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Obter(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await obterUseCase.ExecuteAsync(emp, id, ct)));

    [SwaggerOperation(Summary = "Create campaign",
        Description = "Sem disparoEm fica rascunho. Com disparoEm agenda: exige disparo futuro, mensagem, público e tamanhoOnda > 0.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> Criar([FromBody] CampanhaBody body, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp =>
        {
            var campanha = await criarUseCase.ExecuteAsync(Comando(emp, body), ct);
            return DataCreated($"/api/campanhas/{campanha.Id}", campanha);
        });

    [SwaggerOperation(Summary = "Update campaign",
        Description = "Só rascunho ou agendada. Sem disparoEm a agendada volta a rascunho; com disparoEm reagenda.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Atualizar(Guid id, [FromBody] CampanhaBody body, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await atualizarUseCase.ExecuteAsync(id, Comando(emp, body), ct)));

    [SwaggerOperation(Summary = "Cancel campaign",
        Description = "Destinatários pendentes viram excluídos (motivo cancelada); os já enviados ficam como estão.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/cancelar")]
    public Task<IActionResult> Cancelar(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await cancelarUseCase.ExecuteAsync(emp, id, ct)));

    [SwaggerOperation(Summary = "Recalculate campaign audience",
        Description = "Refaz pendentes e excluídos (bloqueado, sem_consentimento, restricao, limite_semanal, sem_telefone) " +
                      "e preserva quem já saiu. Devolve total, pendentes, excluídos por motivo e amostra de 10 nomes.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/publico")]
    public Task<IActionResult> CalcularPublico(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await calcularPublicoUseCase.ExecuteAsync(emp, id, ct)));

    [SwaggerOperation(Summary = "List campaign recipients (by name, up to 500)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}/destinatarios")]
    public Task<IActionResult> ListarDestinatarios(
        Guid id, [FromQuery] StatusCampanhaDestinatario? status, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await listarDestinatariosUseCase.ExecuteAsync(emp, id, status, ct)));

    private SalvarCampanhaCommand Comando(Guid empresaId, CampanhaBody body)
    {
        var filtro = body.Filtro is { } f
            ? new FiltroCampanha(f.Todos, f.TagsIncluir ?? [], f.TagsExcluir ?? [], f.ComprouItemId, f.ComprouNosUltimosDias)
            : FiltroCampanha.Vazio;
        var dados = new DadosCampanha(
            body.Nome ?? string.Empty, body.Mensagem ?? string.Empty, body.ImagemUrl, body.TemplateMeta, filtro,
            body.TagsRestricaoExcluidas ?? [], body.EncerramentoEm, body.EnviarLembreteEncerramento, body.TamanhoOnda);
        return new SalvarCampanhaCommand(empresaId, currentUser.UsuarioId, dados, body.DisparoEm);
    }

    /// <summary>Resolve a empresa e traduz as exceções em 400/404.</summary>
    private async Task<IActionResult> Executar(Guid? empresaId, Func<Guid, Task<IActionResult>> acao)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        try
        {
            return await acao(emp);
        }
        catch (CampanhaNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

/// <summary>Filtro de público. Sem filtro não há público: "todos" precisa ser escolhido.</summary>
public sealed record FiltroCampanhaBody(
    bool Todos,
    IReadOnlyList<string>? TagsIncluir,
    IReadOnlyList<string>? TagsExcluir,
    Guid? ComprouItemId,
    int? ComprouNosUltimosDias);

public sealed record CampanhaBody(
    string? Nome,
    string? Mensagem,
    string? ImagemUrl,
    string? TemplateMeta,
    FiltroCampanhaBody? Filtro,
    IReadOnlyList<string>? TagsRestricaoExcluidas,
    DateTime? DisparoEm,
    DateTime? EncerramentoEm,
    bool EnviarLembreteEncerramento,
    int? TamanhoOnda);
