using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Comanda do console (F03, ADR-0054): cardápio e janelas da vitrine da empresa logada, o pedido da
/// conversa (S10) com a cobrança (S11) e o estado do pedido e do pagamento para o polling. A empresa vem
/// sempre do token; conversa de outra empresa é 404. Gerar o pedido mexe na conversa e exige
/// <see cref="Permissao.AtenderConversas"/> (S41).
/// </summary>
[SwaggerTag("Attendance order ticket (console)")]
[ApiController]
[Route("api/atendimento")]
[Authorize(Policy = "Operador")]
public class AtendimentoComandaController(
    ListarCardapioComandaUseCase cardapioUseCase,
    ListarJanelasAtendimentoUseCase janelasUseCase,
    ObterPedidoConversaUseCase obterPedidoUseCase,
    GerarPedidoConversaUseCase gerarPedidoUseCase,
    SimularPedidoConversaUseCase simularPedidoUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Menu of the logged company's storefront (same as the public menu)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("comanda/cardapio")]
    public Task<IActionResult> Cardapio(CancellationToken ct = default)
        => Tratar(async () => DataOk(await cardapioUseCase.ExecuteAsync(currentUser.EmpresaId, ct)));

    [SwaggerOperation(Summary = "Delivery windows with vacancies that respect the order lead time (S16)",
        Description = "itens: cardapio_item_id da comanda (o prazo sai do maior preparo). Sem datas, os próximos 14 dias.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet("comanda/janelas")]
    public Task<IActionResult> Janelas(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, [FromQuery] Guid[]? itens,
        CancellationToken ct = default)
        => Tratar(async () => DataOk(await janelasUseCase.ExecuteAsync(
            new ListarJanelasAtendimentoInput(currentUser.EmpresaId, dataInicio, dataFim, itens ?? []), ct)));

    [SwaggerOperation(Summary = "Current order of the conversation with its payment (polling)",
        Description = "data nulo quando a conversa ainda não tem pedido.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("conversas/{id:guid}/pedido")]
    public Task<IActionResult> Pedido(Guid id, CancellationToken ct = default)
        => Tratar(async () => DataOk(await obterPedidoUseCase.ExecuteAsync(currentUser.EmpresaId, id, ct)));

    [SwaggerOperation(Summary = "Create the conversation order, charge it and send the summary to the customer",
        Description = "forma: online (link do Mercado Pago, padrão) ou na_entrega. Sem enderecoId vale o padrão do cliente. enviarAoCliente: false gera para copiar, sem mensagem; padrão true.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("conversas/{id:guid}/pedido")]
    public Task<IActionResult> GerarPedido(Guid id, [FromBody] GerarPedidoConversaBody body, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.AtenderConversas))
            return Task.FromResult<IActionResult>(Forbid());
        if (body?.Itens is not { Count: > 0 })
            return Task.FromResult(DataBadRequest("A comanda está vazia."));

        return Tratar(async () => DataOk(await gerarPedidoUseCase.ExecuteAsync(new GerarPedidoConversaInput(
            currentUser.EmpresaId, id,
            body.Itens.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd, i.Observacao, i.VariacaoId)).ToList(),
            body.JanelaId, body.DataEntrega, body.EnderecoId, body.Forma, body.Observacoes,
            EnviarAoCliente: body.EnviarAoCliente), ct)));
    }

    [SwaggerOperation(Summary = "Quote of the order before creating it: freight, total, deposit and rest (#1884)",
        Description = "Só lê: não cria pedido, não ocupa vaga e não cobra. Mesmas validações e mesma conta do POST do pedido; "
                      + "CEP fora da área devolve freteACombinar com total, sinal e restante nulos. Sinal e restante só na encomenda (entrega em outro dia).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost("conversas/{id:guid}/pedido/simulacao")]
    public Task<IActionResult> SimularPedido(Guid id, [FromBody] SimularPedidoConversaBody body, CancellationToken ct = default)
    {
        // Só lê, como o GET do pedido: qualquer operador da empresa vê o que a comanda custaria; criar é que exige AtenderConversas.
        if (body?.Itens is not { Count: > 0 })
            return Task.FromResult(DataBadRequest("A comanda está vazia."));

        return Tratar(async () => DataOk(await simularPedidoUseCase.ExecuteAsync(new SimularPedidoConversaInput(
            currentUser.EmpresaId, id,
            body.Itens.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd, i.Observacao, i.VariacaoId)).ToList(),
            body.JanelaId, body.DataEntrega, body.EnderecoId), ct)));
    }

    private async Task<IActionResult> Tratar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (ConversaNaoEncontradaException)
        {
            return DataNotFound("Conversa não encontrada.");
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
        catch (Exception ex) when (ex is UseCaseValidationException or CepInvalidoException)
        {
            return DataBadRequest(ex.Message);
        }
        catch (CobrancaPedidoConflitoException ex)
        {
            return DataConflict(ex.Message, ex.Codigo);
        }
        catch (MercadoPagoIndisponivelException ex)
        {
            // #1301: o pedido e a vaga já foram desfeitos; a operadora gera de novo.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError("pagamento_indisponivel", ex.Message, null, null)));
        }
        catch (RegraDeDominioVioladaException ex)
        {
            // #1942: recusa definitiva tem código próprio e o console não relê; PEDIDO_EM_ANDAMENTO (a corrida
            // do clique duplo) e o CONFLICT genérico relêem, porque o pedido pode existir.
            return Recusa(ex) is { } recusa
                ? base.Conflict(new ApiErrorResponse(new ApiError(recusa.Codigo, recusa.Mensagem, null, null)))
                : DataConflict(ex.Message);
        }
    }

    private const string MensagemLojaFechada = "A loja está fechada. Abra a loja para criar o pedido.";
    private const string MensagemClienteBloqueado = "Cliente bloqueado: desbloqueie o cadastro para criar o pedido.";

    // A mensagem da loja fechada é a que a dona escreveu para o cliente, e a do bloqueio traz o id: a atendente
    // lê o que fazer. As outras seguem iguais (o console troca a do CEP pela frase da área de entrega).
    private static (string Codigo, string Mensagem)? Recusa(RegraDeDominioVioladaException ex) => ex switch
    {
        LojaFechadaException => ("LOJA_FECHADA", MensagemLojaFechada),
        JanelaSemVagasException => ("JANELA_ESGOTADA", ex.Message),
        CepSemCoberturaException => ("CEP_SEM_COBERTURA", ex.Message),
        ClienteBloqueadoException => ("CLIENTE_BLOQUEADO", MensagemClienteBloqueado),
        PedidoEmAndamentoException => ("PEDIDO_EM_ANDAMENTO", ex.Message),
        _ => null,
    };
}

/// <summary>Comanda fechada pela operadora. <c>Forma</c>: <c>online</c> (padrão) ou <c>na_entrega</c>.</summary>
public sealed record GerarPedidoConversaBody(
    IReadOnlyList<ItemComandaBody> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null,
    string? Forma = null,
    string? Observacoes = null,
    bool EnviarAoCliente = true);

/// <summary>Comanda a simular (#1884): o mesmo corpo do pedido, sem forma de pagamento nem envio.</summary>
public sealed record SimularPedidoConversaBody(
    IReadOnlyList<ItemComandaBody> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null);

/// <param name="VariacaoId">M1.4b (#1531): a porção escolhida; null em prato com porções usa a padrão.</param>
public sealed record ItemComandaBody(Guid CardapioItemId, int Qtd, string? Observacao = null, Guid? VariacaoId = null);
