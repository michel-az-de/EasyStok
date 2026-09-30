using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>criar_pedido</c> (S06 ligada na S11): fecha o pedido da conversa pelo núcleo do checkout (S10,
/// <see cref="CriarPedidoAtendimentoUseCase"/>) e cobra pelo Mercado Pago (<see cref="GerarCobrancaPedidoUseCase"/>).
/// Devolve resumo, total e o link; o agente responde ao cliente, e a resposta sai pela porta do canal da
/// conversa como toda mensagem do agente.
///
/// <para>
/// Janela e endereço são opcionais: sem janela, vale a única ativa do dia; sem endereço, o padrão (ou o
/// único) do cliente. Havendo mais de uma opção, devolve a lista para o agente perguntar
/// (<c>listar_janelas</c>, S16, oferece só janelas no prazo; <c>validar_endereco</c> chega na S14).
/// </para>
/// </summary>
public sealed class CriarPedidoFerramenta(
    CriarPedidoAtendimentoUseCase criarPedido,
    GerarCobrancaPedidoUseCase gerarCobranca,
    IClienteRepository clienteRepository,
    IStorefrontRepository storefrontRepository,
    IJanelaEntregaRepository janelaRepository) : IFerramentaAgente
{
    public string Nome => "criar_pedido";

    public string Descricao =>
        "Cria o pedido do cliente desta conversa e gera o link de pagamento (Pix ou cartão, válido por 30 minutos). " +
        "Use os ids de consultar_cardapio. Devolve resumo, total e link; se faltar janela ou endereço, devolve as opções " +
        "para você perguntar ao cliente.";

    public string SchemaJson => """
        {"type":"object","properties":{
          "itens":{"type":"array","minItems":1,"items":{"type":"object","properties":{
            "cardapio_item_id":{"type":"string"},
            "quantidade":{"type":"integer","minimum":1},
            "observacao":{"type":"string"}},
            "required":["cardapio_item_id","quantidade"],"additionalProperties":false}},
          "data_entrega":{"type":"string","description":"AAAA-MM-DD"},
          "janela_id":{"type":"string"},
          "endereco_id":{"type":"string"},
          "observacoes":{"type":"string"}},
         "required":["itens","data_entrega"],"additionalProperties":false}
        """;

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var conversa = contexto.Conversa;
        if (conversa.ClienteId is not { } clienteId)
            return Erro("cliente_nao_identificado");

        var itens = LerItens(entrada);
        if (itens is null)
            return Erro("itens_invalidos");

        if (!DateOnly.TryParseExact(FerramentaJson.LerTexto(entrada, "data_entrega"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataEntrega))
            return Erro("data_entrega_invalida");

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(contexto.EmpresaId, clienteId);
        if (cliente is null)
            return Erro("cliente_nao_identificado");
        if (cliente.Bloqueado)
            return Erro(ClienteBloqueadoException.CodigoErro); // S24: o motivo é interno

        var enderecoId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "endereco_id"), out var informado)
            ? informado
            : CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente);
        if (enderecoId is null)
        {
            return FerramentaJson.Serializar(new
            {
                erro = "informe_endereco",
                enderecos = cliente.Enderecos.Select(e => new { id = e.Id, endereco = $"{e.Logradouro}, {e.Numero} {e.Bairro}".Trim() })
            });
        }

        var janelaId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "janela_id"), out var janelaInformada)
            ? janelaInformada
            : (Guid?)null;
        if (janelaId is null)
        {
            var janelas = await JanelasDoDiaAsync(contexto.EmpresaId, dataEntrega, ct);
            if (janelas.Count != 1)
                return FerramentaJson.Serializar(new { erro = "informe_janela", janelas = janelas.Select(j => new { id = j.Id, janela = j.Label }) });
            janelaId = janelas[0].Id;
        }

        PedidoReservado reservado;
        try
        {
            reservado = await criarPedido.ExecuteAsync(new CriarPedidoAtendimentoInput(
                contexto.EmpresaId, conversa.Id, clienteId, itens, janelaId.Value, dataEntrega, enderecoId.Value,
                FerramentaJson.LerTexto(entrada, "observacoes")), ct);
        }
        catch (ClienteBloqueadoException)
        {
            return Erro(ClienteBloqueadoException.CodigoErro);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return FerramentaJson.Serializar(new { erro = "pedido_recusado", motivo = ex.Message });
        }

        var resumo = new
        {
            pedidoId = reservado.Pedido.Id,
            itens = reservado.Itens.Select(i => new
            {
                nome = i.Nome,
                quantidade = i.Quantidade,
                subtotal = FerramentaJson.FormatarReais(i.Subtotal),
                observacao = i.Observacao,
            }),
            frete = FerramentaJson.FormatarReais(reservado.ItemFrete.PrecoUnitario),
            total = FerramentaJson.FormatarReais(reservado.Total),
            entrega = dataEntrega.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        };

        try
        {
            var cobranca = await gerarCobranca.ExecuteAsync(reservado, conversa.Id, ct);
            return FerramentaJson.Serializar(new
            {
                resumo.pedidoId,
                resumo.itens,
                resumo.frete,
                resumo.total,
                resumo.entrega,
                linkPagamento = cobranca.LinkPagamento,
                validoAte = cobranca.ExpiraEm is { } expira
                    ? HorarioBrasil.ConverterParaBrasilia(expira).ToString("HH:mm", CultureInfo.InvariantCulture)
                    : null,
                formasPagamento = "Pix ou cartão, como preferir",
            });
        }
        catch (MercadoPagoIndisponivelException)
        {
            // Pedido criado e vaga reservada; o link sai pela reemissão (dona ou job).
            return FerramentaJson.Serializar(new
            {
                erro = "pagamento_indisponivel",
                resumo.pedidoId,
                resumo.total,
                orientacao = "Pedido anotado, mas o link de pagamento não saiu agora. Avise que a equipe envia o link em instantes e use escalar_para_dona.",
            });
        }
    }

    private static string Erro(string codigo) => FerramentaJson.Serializar(new { erro = codigo });

    private static List<ItemPedidoCheckout>? LerItens(JsonElement entrada)
    {
        if (entrada.ValueKind != JsonValueKind.Object
            || !entrada.TryGetProperty("itens", out var lista)
            || lista.ValueKind != JsonValueKind.Array)
            return null;

        var itens = new List<ItemPedidoCheckout>();
        foreach (var item in lista.EnumerateArray())
        {
            if (!Guid.TryParse(FerramentaJson.LerTexto(item, "cardapio_item_id"), out var cardapioItemId)
                || !item.TryGetProperty("quantidade", out var qtd)
                || qtd.ValueKind != JsonValueKind.Number
                || !qtd.TryGetInt32(out var quantidade)
                || quantidade < 1)
                return null;
            itens.Add(new ItemPedidoCheckout(cardapioItemId, quantidade, FerramentaJson.LerTexto(item, "observacao")));
        }
        return itens.Count == 0 ? null : itens;
    }

    private async Task<IReadOnlyList<Domain.Entities.Storefront.JanelaEntrega>> JanelasDoDiaAsync(
        Guid empresaId, DateOnly data, CancellationToken ct)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null) return [];
        var janelas = await janelaRepository.GetAtivasDoStorefrontAsync(storefront.Id, ct);
        return janelas.Where(j => j.DiaDaSemana == (int)data.DayOfWeek).OrderBy(j => j.HoraInicio).ToList();
    }
}
