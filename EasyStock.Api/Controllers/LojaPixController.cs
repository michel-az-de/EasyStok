using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Pagamentos.PixLoja;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <param name="Tipo"><c>cpf</c>, <c>cnpj</c>, <c>email</c>, <c>telefone</c> ou <c>aleatoria</c>.</param>
public sealed record DefinirChavePixLojaRequest(string? Tipo, string? Chave, string? NomeRecebedor, string? Cidade)
{
    /// <summary>Esconde a chave: o corpo pode cair num log estruturado.</summary>
    public override string ToString() =>
        $"DefinirChavePixLojaRequest {{ Tipo = {Tipo}, Chave = ***, NomeRecebedor = {NomeRecebedor}, Cidade = {Cidade} }}";
}

/// <summary>
/// Chave Pix da loja para o Pix direto (#2233), sem Mercado Pago. A resposta nunca traz a chave inteira: só o tipo e o
/// final. Cobrar pelo Pix direto ainda exige a flag <c>pagamento.pix_direto</c> do tenant, desligada por padrão.
/// </summary>
[SwaggerTag("Stores / Lojas")]
[ApiController]
[Route("api/lojas/{lojaId:guid}/pix")]
[Authorize]
public sealed class LojaPixController(ChavePixLojaUseCases useCases, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Chave Pix da loja (mascarada)", Description = "configurada=false sem chave; pixDiretoLigado é a flag do tenant.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Operador")]
    [HttpGet]
    public async Task<IActionResult> Obter(Guid lojaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error)) return error!;
        var r = await useCases.ObterAsync(empresaId, lojaId, ct);
        return r is null ? DataNotFound("Loja não encontrada.") : DataOk(r);
    }

    [SwaggerOperation(Summary = "Pix direto disponível", Description = "true só com a flag pagamento.pix_direto ligada e alguma loja com chave. Não traz chave.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Authorize(Policy = "Operador")]
    [HttpGet("/api/pix-direto")]
    public async Task<IActionResult> Disponivel(CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error)) return error!;
        return DataOk(new { disponivel = await useCases.DisponivelAsync(empresaId, ct) });
    }

    [SwaggerOperation(Summary = "Grava a chave Pix da loja", Description = "Valida a chave pelo tipo; nome até 25 e cidade até 15 caracteres, sem acento.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [RequestSizeLimit(4 * 1024)]
    [HttpPut]
    public async Task<IActionResult> Definir(Guid lojaId, [FromBody] DefinirChavePixLojaRequest request, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error)) return error!;
        try
        {
            var r = await useCases.DefinirAsync(
                new DefinirChavePixLojaCommand(empresaId, lojaId, request.Tipo, request.Chave, request.NomeRecebedor, request.Cidade), ct);
            return r is null ? DataNotFound("Loja não encontrada.") : DataOk(r);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }

    [SwaggerOperation(Summary = "Remove a chave Pix da loja", Description = "Sem chave, o Pix direto some do console; cobranças já criadas não mudam.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [HttpDelete]
    public async Task<IActionResult> Remover(Guid lojaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error)) return error!;
        var r = await useCases.RemoverAsync(empresaId, lojaId, ct);
        return r is null ? DataNotFound("Loja não encontrada.") : DataOk(r);
    }
}
