using EasyStock.Api.Authentication;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/atendimento/conversas/{conversaId:guid}/comandas-cliente")]
[Authorize(Policy = "Operador")]
public sealed class AtendimentoComandasClienteController(ComandaClienteUseCase comanda, ConfirmarAlteracaoComandaUseCase confirmar,
    ICurrentUserAccessor usuario) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(Guid conversaId, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        if (await comanda.ListarDoAtendimentoAsync(usuario.EmpresaId, conversaId, ct) is not { } resultado) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { data = resultado });
    }

    [HttpPost("{linkId:guid}/confirmar")]
    public async Task<IActionResult> Confirmar(Guid conversaId, Guid linkId, [FromBody] ConfirmarAlteracaoClienteBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        try { return Ok(new { data = await confirmar.ExecuteAsync(new(usuario.EmpresaId, conversaId, linkId, body.Aceitar,
            usuario.UsuarioId, User.NomeExibicao(), usuario.Nivel), ct) }); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
        catch (UnauthorizedAccessException e) { return Problem(e.Message, statusCode: 403); }
    }

    [HttpPost("{linkId:guid}/combinar-frete")]
    public async Task<IActionResult> CombinarFrete(Guid conversaId, Guid linkId, [FromBody] CombinarFreteComandaBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        if (body.Valor is not { } valor) return Problem("Informe o valor do frete combinado.", statusCode: 400);
        try { return Ok(new { data = await comanda.CombinarFreteAsync(usuario.EmpresaId, conversaId, linkId, body.Revisao,
            valor, User.NomeExibicao() ?? "a equipe", ct) }); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
    }

    [HttpPost("{linkId:guid}/encaminhar-entrega")]
    public async Task<IActionResult> EncaminharEntrega(Guid conversaId, Guid linkId,
        [FromBody] EasyStock.Api.Controllers.Storefront.EnviarComandaClienteBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        try { return Ok(new { data = await comanda.EncaminharEntregaDoAtendimentoAsync(usuario.EmpresaId, conversaId,
            linkId, body.Revisao, ct) }); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
        catch (RegraDeDominioVioladaException e) { return Problem(e.Message, statusCode: 422); }
    }

    [HttpPost("{linkId:guid}/confirmar-pedido")]
    public async Task<IActionResult> ConfirmarPedido(Guid conversaId, Guid linkId,
        [FromBody] EasyStock.Api.Controllers.Storefront.EnviarComandaClienteBody body, CancellationToken ct)
    {
        if (!usuario.TemPermissao(Permissao.AtenderConversas)) return Forbid();
        try { return Ok(new { data = await comanda.ConfirmarPedidoDoAtendimentoAsync(usuario.EmpresaId, conversaId,
            linkId, body.Revisao, ct) }); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
        catch (RegraDeDominioVioladaException e) { return Problem(e.Message, statusCode: 422); }
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
public sealed record CombinarFreteComandaBody(int Revisao, decimal? Valor);
