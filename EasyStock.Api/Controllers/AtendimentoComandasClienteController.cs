using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/atendimento/conversas/{conversaId:guid}/comandas-cliente")]
[Authorize(Policy = "Operador")]
public sealed class AtendimentoComandasClienteController(ILinkCardapioConversaRepository links,
    IConversaRepository conversas, ComandaClienteUseCase comanda, ConfirmarAlteracaoComandaUseCase confirmar,
    ICurrentUserAccessor usuario, TimeProvider relogio) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(Guid conversaId, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        if (await conversas.ObterPorIdAsync(usuario.EmpresaId, conversaId, ct) is null) return NotFound();
        var agora = relogio.GetUtcNow().UtcDateTime;
        var lista = await links.ListarDaConversaAsync(usuario.EmpresaId, conversaId, agora.AddDays(-1), ct);
        var resultado = new List<ComandaClienteResult>();
        foreach (var link in lista.Where(l => l.Revisao > 0 || l.EstaValido(agora))) resultado.Add(await comanda.EstadoAsync(link, ct));
        Response.Headers.CacheControl = "no-store";
        return Ok(new { data = resultado });
    }

    [HttpPost("{linkId:guid}/confirmar")]
    public async Task<IActionResult> Confirmar(Guid conversaId, Guid linkId, [FromBody] ConfirmarAlteracaoClienteBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        try { return Ok(new { data = await confirmar.ExecuteAsync(new(usuario.EmpresaId, conversaId, linkId, body.Aceitar,
            usuario.UsuarioId, User.Identity?.Name, usuario.Nivel), ct) }); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
        catch (UnauthorizedAccessException e) { return Problem(e.Message, statusCode: 403); }
    }

    [HttpPost("{linkId:guid}/combinar-frete")]
    public async Task<IActionResult> CombinarFrete(Guid conversaId, Guid linkId, [FromBody] CombinarFreteComandaBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        try { return Ok(new { data = await comanda.CombinarFreteAsync(usuario.EmpresaId, conversaId, linkId, body.Revisao,
            body.Valor, User.Identity?.Name ?? "a equipe", ct) }); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
    }

    [HttpPut("{linkId:guid}/rascunho")]
    public async Task<IActionResult> Salvar(Guid conversaId, Guid linkId,
        [FromBody] EasyStock.Api.Controllers.Storefront.SalvarComandaClienteBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        try { return Ok(new { data = await comanda.SalvarDoAtendimentoAsync(usuario.EmpresaId, conversaId, linkId,
            body.Revisao, body.Rascunho, ct) }); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
    }
}
public sealed record ConfirmarAlteracaoClienteBody(bool Aceitar);
public sealed record CombinarFreteComandaBody(int Revisao, decimal Valor);
