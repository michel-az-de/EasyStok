using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>enviar_link_pagamento</c> (#2544, S74): gera a cobrança do pedido da conversa pelo
/// <see cref="GerarCobrancaPedidoUseCase"/> e manda o link (Pix ou cartão) como resposta do turno. Só depois de o cliente
/// responder ao resumo (<see cref="ResumoPedidoAtendimento"/>). Serve também para o link vencido: a cobrança pendente
/// vencida dá lugar a outra.
/// </summary>
public sealed class EnviarLinkPagamentoFerramenta(
    IPedidoRepository pedidoRepository,
    GerarCobrancaPedidoUseCase gerarCobranca,
    IConversaRepository conversaRepository) : IFerramentaAgente, IFerramentaForaDaTransacao
{
    public string Nome => "enviar_link_pagamento";

    public string Descricao =>
        "Manda ao cliente o link de pagamento do pedido (Pix ou cartão, válido por 30 minutos). Use só depois de o cliente " +
        "dar o ok ao resumo de enviar_resumo_pedido, ou quando ele disser que o link venceu. O link sai como sua resposta.";

    public string SchemaJson =>
        """{"type":"object","properties":{"pedidoId":{"type":"string","description":"id devolvido por criar_pedido"}},"required":["pedidoId"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (entrada.ValueKind != JsonValueKind.Object
            || !Guid.TryParse(FerramentaJson.LerTexto(entrada, "pedidoId"), out var pedidoId))
            return Recusa("pedido_nao_encontrado", "Use o pedidoId que criar_pedido devolveu.");

        // Só pedido do cliente desta conversa: id inventado ou de outra pessoa não gera cobrança.
        var pedido = await pedidoRepository.GetByIdAsync(contexto.EmpresaId, pedidoId);
        if (pedido is null || contexto.ClienteIdEsperado is not { } clienteId || pedido.ClienteId != clienteId)
            return Recusa("pedido_nao_encontrado", "Use o pedidoId que criar_pedido devolveu.");
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            return Recusa("pedido_finalizado",
                "O pedido foi cancelado ou já foi entregue. Se o cliente ainda quiser, monte de novo com criar_pedido.");

        var mensagens = await conversaRepository.ListarMensagensDepoisAsync(
            contexto.EmpresaId, contexto.Conversa.Id, pedido.CriadoEm.AddSeconds(-1), 500, ct);
        var resumo = mensagens.LastOrDefault(m => m.Direcao == DirecaoMensagem.Saida && m.Status != StatusMensagem.Falhou
            && ResumoPedidoAtendimento.EhResumo(m.Texto, pedidoId));
        if (resumo is null)
            return Recusa("resumo_nao_enviado", "Mande o resumo com enviar_resumo_pedido e peça o ok do cliente antes do link.");
        if (!mensagens.Any(m => m.Direcao == DirecaoMensagem.Entrada && m.EnviadaEm > resumo.EnviadaEm))
            return Recusa("aguardando_ok", "O resumo já foi mandado. Espere o cliente confirmar; o link sai depois do ok.");

        CobrancaPedidoResult cobranca;
        try
        {
            cobranca = await gerarCobranca.ExecuteComTravaAsync(
                new GerarCobrancaPedidoInput(contexto.EmpresaId, pedidoId, contexto.Conversa.Id), ct);
        }
        catch (CobrancaPedidoConflitoException ex) when (ex.Codigo == CobrancaPedidoConflitoException.PedidoJaPago)
        {
            return Recusa("pedido_ja_pago", "O pedido já está pago; não mande link. Confira com consultar_pedido e avise o cliente.");
        }
        catch (CobrancaPedidoConflitoException ex) when (ex.Codigo == CobrancaPedidoConflitoException.PedidoFinalizado)
        {
            return Recusa("pedido_finalizado",
                "O pedido foi cancelado ou já foi entregue. Se o cliente ainda quiser, monte de novo com criar_pedido.");
        }
        catch (CobrancaPedidoConflitoException ex)
        {
            return Recusa(ex.Codigo, "O link não pode sair por aqui; use escalar_para_dona.");
        }
        catch (CobrancaPedidoNaoEncontradoException)
        {
            return Recusa("pedido_nao_encontrado", "Use o pedidoId que criar_pedido devolveu.");
        }
        catch (MercadoPagoIndisponivelException)
        {
            return Recusa("pagamento_indisponivel",
                "O link não saiu agora. Peça um instante ao cliente e use enviar_link_pagamento de novo; se falhar outra vez, use escalar_para_dona.");
        }

        if (string.IsNullOrWhiteSpace(cobranca.LinkPagamento))
            return Recusa("sem_link", "O pedido não tem link de pagamento; use escalar_para_dona.");

        var valor = FerramentaJson.FormatarReais(cobranca.Valor);
        var validoAte = cobranca.ExpiraEm is { } expira
            ? HorarioBrasil.ConverterParaBrasilia(expira).ToString("HH:mm", CultureInfo.InvariantCulture)
            : null;
        contexto.RegistrarRespostaVerificada(
            $"{QuantoCobra(pedido.Total.Valor, cobranca.Valor)} Pague por Pix ou cartão neste link{(validoAte is null ? "" : $", válido até {validoAte}")}: {cobranca.LinkPagamento}");
        return FerramentaJson.Serializar(new
        {
            linkGerado = true,
            valor,
            validoAte,
            orientacao = "O link sai ao cliente como sua resposta deste turno.",
        });
    }

    // #2544: na encomenda com sinal o link cobra só uma parte; chamar a parte de "Total" engana o cliente.
    private static string QuantoCobra(decimal totalPedido, decimal cobrado)
    {
        var valor = FerramentaJson.FormatarReais(cobrado);
        if (cobrado >= totalPedido) return $"Total: {valor}.";
        var total = FerramentaJson.FormatarReais(totalPedido);
        return cobrado == PlanoPagamentoEncomenda.Sinal(totalPedido)
            ? $"Total do pedido: {total}. Agora é o sinal de {valor}; o restante é pago depois."
            : $"Total do pedido: {total}. Este link é de {valor}, o que falta pagar.";
    }

    private static string Recusa(string motivo, string orientacao) =>
        FerramentaJson.Serializar(new { linkGerado = false, motivo, orientacao });
}
