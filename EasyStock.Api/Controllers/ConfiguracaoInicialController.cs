using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.PrimeirosPassos;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Corpo do <c>PUT api/configuracao-inicial/passos/{passo}</c>: <c>conferido</c> ou <c>depois</c>.</summary>
public sealed record MarcarPassoConfiguracaoInicialBody(string? Situacao);

/// <summary>
/// Configuração inicial da loja no servidor (#2070): o estado do assistente "Primeiros passos" do console. Só guarda
/// marcas; cada passo grava pelas rotas que já existem. Empresa sempre do JWT; sem ela, 400.
/// </summary>
[SwaggerTag("Store first-time setup")]
[ApiController]
[Route("api/configuracao-inicial")]
[Authorize(Policy = "Admin")]
public class ConfiguracaoInicialController(
    ObterConfiguracaoInicialUseCase obterUseCase,
    MarcarPassoConfiguracaoInicialUseCase marcarUseCase,
    AdiarConfiguracaoInicialUseCase adiarUseCase,
    ConcluirConfiguracaoInicialUseCase concluirUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    private const string EmpresaObrigatoria = "EmpresaId é obrigatório.";

    [SwaggerOperation(Summary = "First-time setup state of the store (Admin)",
        Description = "pendente, abrirSozinho (dona, pendente e não adiada), ehDona, podeConcluir (dona ou suporte), concluidaEm, adiadaEm e os passos do catálogo com parte, ordem, situacao, em, porNome, disponivel, motivo e prontidao ({ok, falta[]} nos essenciais).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken ct)
    {
        if (currentUser.EmpresaId == Guid.Empty) return DataBadRequest(EmpresaObrigatoria);
        return DataOk(await obterUseCase.ExecuteAsync(ct));
    }

    [SwaggerOperation(Summary = "Mark a setup step as checked or for later (Admin)",
        Description = "Body {situacao: 'conferido'|'depois'}. Só marca o passo, por upsert; nunca grava configuração. Passo fora do catálogo: 400 PASSO_DESCONHECIDO.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPut("passos/{passo}")]
    public async Task<IActionResult> Marcar(string passo, [FromBody] MarcarPassoConfiguracaoInicialBody body, CancellationToken ct)
    {
        if (currentUser.EmpresaId == Guid.Empty) return DataBadRequest(EmpresaObrigatoria);
        try
        {
            await marcarUseCase.ExecuteAsync(passo, body?.Situacao, ct);
            return NoContent();
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }

    [SwaggerOperation(Summary = "Postpone the first-time setup (owner or support)",
        Description = "\"Continuar depois\": o assistente não abre sozinho. Quem não é dona nem suporte: 403 HIERARQUIA.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("adiar")]
    public Task<IActionResult> Adiar(CancellationToken ct) => DecidirAsync(() => adiarUseCase.ExecuteAsync(ct));

    [SwaggerOperation(Summary = "Finish the first-time setup (owner or support)",
        Description = "Aceita passos deixados para depois; concluir de novo mantém a primeira conclusão. Quem não é dona nem suporte: 403 HIERARQUIA.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("concluir")]
    public Task<IActionResult> Concluir(CancellationToken ct) => DecidirAsync(() => concluirUseCase.ExecuteAsync(ct));

    private async Task<IActionResult> DecidirAsync(Func<Task> decisao)
    {
        if (currentUser.EmpresaId == Guid.Empty) return DataBadRequest(EmpresaObrigatoria);
        try
        {
            await decisao();
            return NoContent();
        }
        catch (UseCaseValidationException ex) when (ex.Code == AutoridadeNaConfiguracaoInicial.CodigoHierarquia)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiErrors.Validacao(ex));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}
