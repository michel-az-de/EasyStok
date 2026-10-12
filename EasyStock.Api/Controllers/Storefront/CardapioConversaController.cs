using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Cardápio que o cliente marca, ligado à conversa (S48). O link enviado na conversa abre o cardápio
/// do site com <c>?c=&lt;token&gt;</c>; ao enviar o carrinho, o site chama este endpoint com o token e o
/// pedido volta pronto para a conversa. Anônimo: o token (24 h, um pedido só) é a credencial e não
/// carrega telefone, nome nem id. Vencido, já usado ou de conversa encerrada: 410 com a orientação de
/// pedir um link novo na conversa.
/// </summary>
[SwaggerTag("Storefront Cardapio da conversa")]
[ApiController]
[Route("api/storefront/cardapio-conversa/{token}")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Sem cookie: a credencial é o token do link, então CSRF não se aplica.
public sealed class CardapioConversaController(CriarPedidoPeloCardapioConversaUseCase criarPedido) : EasyStockControllerBase
{
    [SwaggerOperation(
        Summary = "Enviar o pedido montado no cardápio da conversa",
        Description = "Cria o pedido pelo caminho da conversa, vincula à conversa com o resumo e gera a cobrança na forma " +
                      "escolhida (online, padrão, ou na_entrega). 410 quando o link venceu ou já foi usado.")]
    [ProducesResponseType(typeof(PedidoPeloCardapioConversaResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting("public-post")]
    [HttpPost("pedido")]
    public async Task<IActionResult> EnviarPedido(
        [FromRoute] string token,
        [FromBody] PedidoCardapioConversaRequestBody body,
        CancellationToken ct)
    {
        if (body?.Itens is not { Count: > 0 })
            return DataBadRequest("Marque ao menos um item do cardápio.");

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true, NoCache = true };
        try
        {
            var result = await criarPedido.ExecuteAsync(new CriarPedidoPeloCardapioConversaInput(
                Token: token,
                Itens: body.Itens.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd, i.Observacao)).ToList(),
                JanelaId: body.JanelaId,
                DataEntrega: body.DataEntrega,
                EnderecoId: body.EnderecoId,
                Forma: body.Forma,
                Observacoes: body.Observacoes, Endereco: body.Endereco), ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (LinkCardapioConversaIndisponivelException ex)
        {
            return Problema(StatusCodes.Status410Gone, "Link do cardápio expirou", ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (CepInvalidoException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is JanelaSemVagasException or LojaFechadaException)
        {
            return Problema(StatusCodes.Status409Conflict,
                ex is LojaFechadaException ? "Loja fechada" : "Janela esgotada", ex.Message);
        }
        catch (MercadoPagoIndisponivelException ex)
        {
            // #1301: pedido desfeito e link devolvido; o cliente reenvia o carrinho.
            return Problema(StatusCodes.Status503ServiceUnavailable, "Gateway de pagamento indisponível", ex.Message);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return Problema(StatusCodes.Status422UnprocessableEntity, "Pedido recusado", ex.Message);
        }
    }

    private ObjectResult Problema(int status, string titulo, string detalhe) =>
        StatusCode(status, new ProblemDetails { Status = status, Title = titulo, Detail = detalhe });
}

/// <summary>Carrinho do cardápio da conversa. <c>Forma</c>: <c>online</c> (padrão) ou <c>na_entrega</c>.</summary>
public sealed record PedidoCardapioConversaRequestBody(
    IReadOnlyList<ItemCardapioConversaRequest> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null,
    string? Forma = null,
    string? Observacoes = null,
    EnderecoCheckout? Endereco = null);

public sealed record ItemCardapioConversaRequest(Guid CardapioItemId, int Qtd, string? Observacao = null);
